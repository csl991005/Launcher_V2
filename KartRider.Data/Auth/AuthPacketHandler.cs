using KartRider.IO.Packet;
using KartRider_PacketName;
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace KartRider;

/// <summary>
/// 登录认证拦截器。
///
/// 挂载方式：AuthBootstrap 通过 [ModuleInitializer] 在进程启动时注册到 PacketDispatcher，
/// 因此不需要修改 ClientSession.OnPacket 这类核心文件。
///
/// 工作方式：
/// - 认证开启时，先校验 PqCnAuthenLogin 携带的账号与口令证明；
///   校验通过则返回 false，交给原版逻辑继续处理（昵称、Profile 目录等行为完全不变）；
///   校验失败则断开连接并返回 true，原版逻辑不会执行。
/// - 同时看住 PqLogin：未通过认证的连接即使跳过登录包直接发 PqLogin，也会被拒绝。
/// </summary>
internal sealed class AuthPacketHandler : IPacketHandler
{
    /// <summary>连接级认证状态，随 SessionGroup 生命周期自动回收。</summary>
    private static readonly ConditionalWeakTable<SessionGroup, AuthSessionState> Sessions =
        new ConditionalWeakTable<SessionGroup, AuthSessionState>();

    public string Name
    {
        get { return "AuthLoginGuard"; }
    }

    /// <summary>优先级高于普通插件，保证先于其它 Mod 判断登录。</summary>
    public int Priority
    {
        get { return 1000; }
    }

    public HashSet<PacketName> TargetPackets { get; } = new HashSet<PacketName>
    {
        PacketName.PqCnAuthenLogin,
        PacketName.PqLogin,
    };

    public bool HandlePacket(PacketContext context)
    {
        if (context == null || !(context.ClientInfo is SessionGroup))
            return false;

        SessionGroup session = (SessionGroup)context.ClientInfo;

        AuthServerConfig config;
        try
        {
            config = AuthStore.GetServerConfig();
        }
        catch (Exception ex)
        {
            // 配置读不出来时不拦截，避免因为 I/O 异常把所有人挡在门外
            Console.WriteLine($"[Auth] 读取认证配置异常: {ex.Message}");
            return false;
        }

        if (!config.Enabled)
            return false;

        string ip = GetClientIp(session);

        // ---- PqLogin：兜底，防止跳过认证包直接登录 ----
        if (context.PacketName == PacketName.PqLogin)
        {
            AuthSessionState state = Sessions.GetOrCreateValue(session);
            if (!state.Authenticated)
            {
                Console.WriteLine($"[Auth] 拒绝未认证的登录请求（PqLogin），来源 {ip}");
                RejectSession(session);
                return true;
            }
            return false;
        }

        // ---- PqCnAuthenLogin ----
        if (config.AllowLoopback && IsLoopback(ip))
        {
            AuthSessionState state = Sessions.GetOrCreateValue(session);
            state.Authenticated = true;
            state.Ip = ip;
            Console.WriteLine($"[Auth] 本机回环连接 {ip} 已按配置放行");
            return false;
        }

        string nickname;
        string account;
        string proof;
        string parseError;
        if (!TryReadLoginPacket(context.Packet, out nickname, out account, out proof, out parseError))
        {
            Console.WriteLine($"[Auth] 认证包解析失败（{ip}）: {parseError}");
            RejectSession(session);
            return true;
        }

        AuthAccount matched;
        string reason;
        if (!AuthStore.Verify(account, proof, ip, out matched, out reason))
        {
            Console.WriteLine($"[Auth] 拒绝登录: 账号 {account}，来源 {ip}，原因：{reason}");
            RejectSession(session);
            return true;
        }

        // 账号可选绑定游戏昵称；未绑定则允许玩家在「设置」里自由填写
        if (matched != null &&
            !string.IsNullOrWhiteSpace(matched.Nickname) &&
            !string.Equals(matched.Nickname.Trim(), nickname, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"[Auth] 拒绝登录: 账号 {account} 绑定的游戏昵称为「{matched.Nickname}」，客户端提交的是「{nickname}」");
            RejectSession(session);
            return true;
        }

        AuthSessionState okState = Sessions.GetOrCreateValue(session);
        okState.Authenticated = true;
        okState.Account = account;
        okState.Ip = ip;

        AuthStore.RecordSuccess(matched, ip);

        // 返回 false：继续走原版登录流程，昵称、Profile 目录等行为与未启用认证时完全一致
        return false;
    }

    // ---------------------------------------------------------------- 解析

    /// <summary>
    /// PqCnAuthenLogin 结构：Adler32(4) + int + int + ASCII 字符串（Base64 的 passport JSON）。
    /// 与服务端原版解析保持一致。
    /// </summary>
    private static bool TryReadLoginPacket(InPacket packet, out string nickname, out string account,
        out string proof, out string error)
    {
        nickname = null;
        account = null;
        proof = null;
        error = null;

        try
        {
            if (packet == null)
            {
                error = "空数据包";
                return false;
            }

            // PacketDispatcher 传入的是完整包副本，Position 停留在 hash 之后（4）
            packet.Position = 4;
            packet.ReadInt();
            packet.ReadInt();

            string encoded = packet.ReadString(true);
            string json = Base64Helper.Decode(encoded);

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "passport 为空或不是合法 Base64";
                return false;
            }

            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    error = "passport 不是 JSON 对象";
                    return false;
                }

                nickname = GetJsonString(root, "Nickname");
                account = GetJsonString(root, "Account");
                proof = GetJsonString(root, "AuthProof");
            }

            if (string.IsNullOrWhiteSpace(nickname))
            {
                error = "passport 缺少 Nickname";
                return false;
            }

            nickname = nickname.Trim();

            if (!AuthStore.IsSafeAccountName(nickname))
            {
                error = "昵称包含非法字符或长度超出限制";
                return false;
            }

            // 兼容旧版启动器：没有 Account 字段时退化为用昵称当账号
            if (string.IsNullOrWhiteSpace(account))
                account = nickname;

            account = account.Trim();

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string GetJsonString(JsonElement root, string propertyName)
    {
        JsonElement element;
        if (!root.TryGetProperty(propertyName, out element))
            return null;

        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();

        if (element.ValueKind == JsonValueKind.Null)
            return null;

        return null;
    }

    // ---------------------------------------------------------------- 工具

    private static string GetClientIp(SessionGroup session)
    {
        try
        {
            if (session == null || session.Client == null)
                return string.Empty;

            IPEndPoint endPoint = session.Client.GetRemoteEndPoint();
            if (endPoint == null || endPoint.Address == null)
                return string.Empty;

            return endPoint.Address.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsLoopback(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return false;

        if (ip == "::1" || ip == "localhost")
            return true;

        // IPv4 映射到 IPv6 的形式（例如 frp 转发）：::ffff:127.0.0.1
        if (ip.StartsWith("::ffff:127.", StringComparison.OrdinalIgnoreCase))
            return true;

        IPAddress address;
        if (IPAddress.TryParse(ip, out address))
            return IPAddress.IsLoopback(address);

        return false;
    }

    /// <summary>
    /// 延迟断开连接。
    /// 当前线程正处在 ClientSession.OnPacket 的锁内，直接 Disconnect 会与接收循环竞争 Socket 状态，
    /// 因此交给线程池稍后执行。
    /// </summary>
    private static void RejectSession(SessionGroup session)
    {
        if (session == null)
            return;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200).ConfigureAwait(false);
                if (session.Client != null)
                    session.Client.Disconnect();
            }
            catch
            {
            }
        });
    }
}
