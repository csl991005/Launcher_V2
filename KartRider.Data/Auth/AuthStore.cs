using Profile;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace KartRider;

/// <summary>
/// 账号库的读写与校验。所有状态都落在 Profile 目录下，不依赖数据库。
/// </summary>
internal static class AuthStore
{
    private static readonly object IoLock = new object();

    /// <summary>失败计数：key = "A:账号" 或 "I:IP"。</summary>
    private static readonly ConcurrentDictionary<string, FailureRecord> Failures =
        new ConcurrentDictionary<string, FailureRecord>(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ServerConfigPath
    {
        get { return Path.Combine(FileName.ProfileDir, "AuthServer.json"); }
    }

    public static string ClientConfigPath
    {
        get { return Path.Combine(FileName.ProfileDir, "AuthClient.json"); }
    }

    private sealed class FailureRecord
    {
        public int Count;
        public long LockUntilTicks;
    }

    // ---------------------------------------------------------------- 服务端配置

    /// <summary>首次运行时生成默认配置（默认不启用，保持原版行为）。</summary>
    public static void EnsureServerConfig()
    {
        try
        {
            lock (IoLock)
            {
                if (!File.Exists(ServerConfigPath))
                {
                    SaveServerConfigUnlocked(CreateDefaultServerConfig());
                    AuthLog.Write($"已生成认证配置文件: {ServerConfigPath}");
                    AuthLog.Write("默认账号 admin / 口令 admin，请在「账号管理」中尽快修改");
                }
            }
        }
        catch (Exception ex)
        {
            AuthLog.Write($"生成认证配置失败: {ex.Message}");
        }
    }

    public static AuthServerConfig CreateDefaultServerConfig()
    {
        AuthServerConfig config = new AuthServerConfig
        {
            Enabled = true,
            AllowLoopback = false,
            MaxFailedAttempts = 5,
            LockoutSeconds = 300,
            LogSuccessfulLogin = true,
        };

        // 默认账号：账号与游戏昵称无关，玩家仍在「设置」里填写自己的昵称
        AuthAccount admin = new AuthAccount
        {
            Account = DefaultAccountName,
            Nickname = string.Empty,
            Enabled = true,
            DefaultPassword = true,
            Note = "默认账号，请尽快修改口令",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        ApplyPassword(admin, DefaultAccountPassword);
        config.Accounts.Add(admin);

        return config;
    }

    /// <summary>首次运行生成的默认账号，仅用于让服务器开箱可用。</summary>
    public const string DefaultAccountName = "admin";

    public const string DefaultAccountPassword = "admin";

    /// <summary>按明文口令写入账号的盐与哈希。服务端不保存明文。</summary>
    public static void ApplyPassword(AuthAccount account, string password)
    {
        string proof = AuthCrypto.ComputeClientProof(account.Account, password);
        string salt = AuthCrypto.CreateSalt();

        account.Salt = salt;
        account.Hash = AuthCrypto.HashProof(proof, salt, AuthCrypto.StoreIterations);
        account.Iterations = AuthCrypto.StoreIterations;
    }

    public static AuthServerConfig GetServerConfig()
    {
        lock (IoLock)
        {
            return LoadServerConfigUnlocked();
        }
    }

    /// <summary>只读尝试：文件不存在或解析失败时返回 null，不会创建文件。</summary>
    public static AuthServerConfig TryReadServerConfigSilently()
    {
        try
        {
            lock (IoLock)
            {
                if (!File.Exists(ServerConfigPath))
                    return null;

                string text = File.ReadAllText(ServerConfigPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                return JsonSerializer.Deserialize<AuthServerConfig>(text, ReadOptions);
            }
        }
        catch
        {
            return null;
        }
    }

    private static AuthServerConfig LoadServerConfigUnlocked()
    {
        try
        {
            if (!File.Exists(ServerConfigPath))
            {
                AuthServerConfig created = CreateDefaultServerConfig();
                SaveServerConfigUnlocked(created);
                return created;
            }

            string text = File.ReadAllText(ServerConfigPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
                return new AuthServerConfig();

            AuthServerConfig config = JsonSerializer.Deserialize<AuthServerConfig>(text, ReadOptions);
            if (config == null)
                return FailClosedConfig("配置为空");

            if (config.Accounts == null)
                config.Accounts = new System.Collections.Generic.List<AuthAccount>();

            return config;
        }
        catch (Exception ex)
        {
            return FailClosedConfig(ex.Message);
        }
    }

    /// <summary>
    /// 配置损坏时采取“失败关闭”：宁可全部拒绝，也不要在不知情的情况下放行。
    /// 同时把损坏文件备份为 .bak，便于人工恢复。
    /// </summary>
    private static AuthServerConfig FailClosedConfig(string message)
    {
        AuthLog.Write($"认证配置无法解析（{message}），已临时拒绝所有登录，请检查 {ServerConfigPath}");

        try
        {
            if (File.Exists(ServerConfigPath))
            {
                string backup = ServerConfigPath + ".bak";
                File.Copy(ServerConfigPath, backup, true);
                AuthLog.Write($"已备份损坏配置到 {backup}");
            }
        }
        catch
        {
        }

        return new AuthServerConfig { Enabled = true };
    }

    public static void SaveServerConfig(AuthServerConfig config)
    {
        lock (IoLock)
        {
            SaveServerConfigUnlocked(config);
        }
    }

    private static void SaveServerConfigUnlocked(AuthServerConfig config)
    {
        string directory = Path.GetDirectoryName(ServerConfigPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(config, WriteOptions);
        string temp = ServerConfigPath + ".tmp";

        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, ServerConfigPath, true);
    }

    // ---------------------------------------------------------------- 校验

    /// <summary>
    /// 校验账号与口令证明。
    /// </summary>
    /// <param name="account">登录账号（与游戏昵称无关）</param>
    /// <param name="proof">客户端 PBKDF2 口令证明</param>
    /// <param name="ip">连接来源 IP，用于失败锁定</param>
    /// <param name="matched">校验通过时返回命中的账号</param>
    /// <param name="reason">失败原因（用于日志，不返回给客户端）</param>
    public static bool Verify(string account, string proof, string ip, out AuthAccount matched, out string reason)
    {
        matched = null;
        reason = string.Empty;

        AuthServerConfig config = GetServerConfig();
        if (!config.Enabled)
            return true;

        account = (account ?? string.Empty).Trim();
        if (account.Length == 0)
        {
            reason = "账号为空";
            return false;
        }

        if (string.IsNullOrEmpty(proof))
        {
            reason = "未携带认证凭据（客户端版本过旧，或在登录框勾选了跳过认证）";
            return false;
        }

        string lockedReason;
        if (IsLockedOut(account, ip, config, out lockedReason))
        {
            reason = lockedReason;
            return false;
        }

        AuthAccount target = config.Accounts.FirstOrDefault(a =>
            a != null && string.Equals(a.Account, account, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            // 恒定开销：账号不存在时也做一次派生，避免通过响应时间枚举账号
            try
            {
                AuthCrypto.HashProof(proof, AuthCrypto.CreateSalt(), AuthCrypto.StoreIterations);
            }
            catch
            {
            }

            RegisterFailure(account, ip, config);
            reason = "账号不存在";
            return false;
        }

        if (!target.Enabled)
        {
            reason = "账号已被禁用";
            return false;
        }

        if (!AuthCrypto.VerifyProof(proof, target.Salt, target.Iterations, target.Hash))
        {
            RegisterFailure(account, ip, config);
            reason = "密码错误";
            return false;
        }

        ClearFailures(account, ip);
        matched = target;
        return true;
    }

    /// <summary>
    /// 认证全部通过后记录登录统计并输出日志。
    /// 单独一步是为了避免"口令对了但昵称被拒"也被记为成功登录。
    /// </summary>
    public static void RecordSuccess(AuthAccount account, string ip)
    {
        if (account == null)
            return;

        try
        {
            AuthServerConfig config = GetServerConfig();
            AuthAccount target = config.Accounts.FirstOrDefault(a =>
                a != null && string.Equals(a.Account, account.Account, StringComparison.OrdinalIgnoreCase));

            if (target == null)
                return;

            target.LastLoginAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            target.LastLoginIp = ip ?? string.Empty;
            target.LoginCount++;

            SaveServerConfig(config);

            if (config.LogSuccessfulLogin)
                AuthLog.Write($"登录成功: 账号 {target.Account} 来自 {ip}（累计 {target.LoginCount} 次）");
        }
        catch (Exception ex)
        {
            // 统计写入失败不应影响玩家登录
            AuthLog.Write($"保存登录统计失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 诊断用校验：忽略“认证开关”和失败锁定，只看账号与口令是否匹配。
    /// 供 Launcher.exe --auth verify 使用。
    /// </summary>
    public static bool Diagnose(string account, string proof, out string reason)
    {
        reason = string.Empty;

        AuthServerConfig config = GetServerConfig();
        account = (account ?? string.Empty).Trim();

        AuthAccount target = config.Accounts.FirstOrDefault(a =>
            a != null && string.Equals(a.Account, account, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            reason = "账号不存在";
            return false;
        }

        if (!target.Enabled)
        {
            reason = "账号已禁用";
            return false;
        }

        if (string.IsNullOrEmpty(proof))
        {
            reason = "口令为空";
            return false;
        }

        if (!AuthCrypto.VerifyProof(proof, target.Salt, target.Iterations, target.Hash))
        {
            reason = "口令不匹配";
            return false;
        }

        reason = "校验通过";
        return true;
    }

    // ---------------------------------------------------------------- 失败锁定
    private static bool IsLockedOut(string account, string ip, AuthServerConfig config, out string reason)
    {
        reason = string.Empty;
        if (config.MaxFailedAttempts <= 0)
            return false;

        long now = Environment.TickCount64;

        FailureRecord byAccount = GetRecord("A:" + account);
        if (byAccount != null && CheckRecord(byAccount, now))
        {
            reason = $"该账号连续登录失败次数过多，请 {config.LockoutSeconds} 秒后再试";
            return true;
        }

        if (!string.IsNullOrEmpty(ip))
        {
            FailureRecord byIp = GetRecord("I:" + ip);
            if (byIp != null && CheckRecord(byIp, now))
            {
                reason = $"该 IP 连续登录失败次数过多，请 {config.LockoutSeconds} 秒后再试";
                return true;
            }
        }

        return false;
    }

    /// <summary>返回 true 表示仍处于锁定期；顺带清理过期的失败计数。</summary>
    private static bool CheckRecord(FailureRecord record, long now)
    {
        lock (record)
        {
            if (record.LockUntilTicks == 0)
                return false;

            if (now < record.LockUntilTicks)
                return true;

            // 锁定到期，重新计数
            record.LockUntilTicks = 0;
            record.Count = 0;
            return false;
        }
    }

    private static FailureRecord GetRecord(string key)
    {
        FailureRecord record;
        return Failures.TryGetValue(key, out record) ? record : null;
    }

    private static void RegisterFailure(string account, string ip, AuthServerConfig config)
    {
        if (config.MaxFailedAttempts <= 0)
            return;

        AddFailure("A:" + account, config);
        if (!string.IsNullOrEmpty(ip))
            AddFailure("I:" + ip, config);
    }

    private static void AddFailure(string key, AuthServerConfig config)
    {
        FailureRecord record = Failures.GetOrAdd(key, _ => new FailureRecord());

        lock (record)
        {
            record.Count++;
            if (record.Count >= config.MaxFailedAttempts)
            {
                record.LockUntilTicks = Environment.TickCount64 + Math.Max(1, config.LockoutSeconds) * 1000L;
                record.Count = 0;
                AuthLog.Write($"{key} 失败次数达到上限，已临时锁定 {config.LockoutSeconds} 秒");
            }
        }
    }

    private static void ClearFailures(string account, string ip)
    {
        FailureRecord removed;
        Failures.TryRemove("A:" + account, out removed);

        if (!string.IsNullOrEmpty(ip))
            Failures.TryRemove("I:" + ip, out removed);
    }

    // ---------------------------------------------------------------- 客户端配置

    public static AuthClientConfig GetClientConfig()
    {
        try
        {
            lock (IoLock)
            {
                if (!File.Exists(ClientConfigPath))
                {
                    AuthClientConfig created = new AuthClientConfig
                    {
                        Enabled = true,
                        LastAccount = DefaultAccountName,
                    };
                    SaveClientConfigUnlocked(created);
                    return created;
                }

                string text = File.ReadAllText(ClientConfigPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                    return new AuthClientConfig();

                AuthClientConfig config = JsonSerializer.Deserialize<AuthClientConfig>(text, ReadOptions);
                return config ?? new AuthClientConfig();
            }
        }
        catch (Exception ex)
        {
            AuthLog.Write($"读取客户端认证配置失败: {ex.Message}");
            return new AuthClientConfig();
        }
    }

    public static void SaveClientConfig(AuthClientConfig config)
    {
        try
        {
            lock (IoLock)
            {
                SaveClientConfigUnlocked(config);
            }
        }
        catch (Exception ex)
        {
            AuthLog.Write($"保存客户端认证配置失败: {ex.Message}");
        }
    }

    private static void SaveClientConfigUnlocked(AuthClientConfig config)
    {
        string directory = Path.GetDirectoryName(ClientConfigPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(config, WriteOptions);
        string temp = ClientConfigPath + ".tmp";

        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, ClientConfigPath, true);
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>账号名同时会作为目录名，需要过滤非法字符。</summary>
    public static bool IsSafeAccountName(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return false;

        string name = account.Trim();
        if (name.Length == 0 || name.Length > 32)
            return false;

        if (name == "." || name == "..")
            return false;

        const string invalid = "/\\:*?\"<>|";
        foreach (char c in name)
        {
            if (c < 32 || invalid.IndexOf(c) >= 0)
                return false;
        }

        return true;
    }
}
