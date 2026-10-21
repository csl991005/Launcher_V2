using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace KartRider;

/// <summary>
/// 账号管理命令行。
///
/// 用法（在 Launcher.exe 所在目录执行）：
///   Launcher.exe --auth add &lt;账号&gt; [口令]     新增账号 / 重置已有账号
///   Launcher.exe --auth passwd &lt;账号&gt; [口令]   修改口令
///   Launcher.exe --auth remove &lt;账号&gt;          删除账号
///   Launcher.exe --auth enable [账号]            启用认证（或启用单个账号）
///   Launcher.exe --auth disable [账号]           关闭认证（或禁用单个账号）
///   Launcher.exe --auth list                     列出全部账号
///
/// 口令留空时会自动生成并打印；口令传 "-" 则交互式输入。
/// </summary>
internal static class AuthCli
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    /// <summary>
    /// 若命令行包含 --auth 则接管执行并退出进程；否则返回 false，交给正常启动流程。
    /// </summary>
    public static bool TryRun(string[] args)
    {
        if (args == null || args.Length == 0)
            return false;

        int index = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--auth", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return false;

        bool allocated = GetConsoleWindow() == IntPtr.Zero;
        if (allocated)
            AllocConsole();

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
        }

        int exitCode = 0;
        try
        {
            exitCode = Execute(args, index);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Auth] 执行失败: " + ex.Message);
            exitCode = 1;
        }

        if (allocated)
        {
            Console.WriteLine();
            Console.WriteLine("按回车键退出...");
            try
            {
                Console.ReadLine();
            }
            catch
            {
            }
        }

        Environment.Exit(exitCode);
        return true;
    }

    private static int Execute(string[] args, int index)
    {
        string command = index + 1 < args.Length ? args[index + 1].ToLowerInvariant() : "help";
        string account = index + 2 < args.Length ? args[index + 2] : null;
        string password = index + 3 < args.Length ? args[index + 3] : null;

        switch (command)
        {
            case "add":
                return AddAccount(account, password);

            case "passwd":
            case "reset":
            case "password":
                return SetPassword(account, password);

            case "remove":
            case "del":
            case "delete":
                return RemoveAccount(account);

            case "list":
            case "ls":
                return ListAccounts();

            case "enable":
                return SetEnabled(true, account);

            case "disable":
                return SetEnabled(false, account);

            case "status":
                return PrintStatus();

            case "verify":
            case "check":
                return VerifyAccount(account, password);

            case "help":
            case "-h":
            case "--help":
            default:
                PrintHelp();
                return command == "help" || command.StartsWith("-") ? 0 : 1;
        }
    }

    // ---------------------------------------------------------------- 子命令

    private static int AddAccount(string account, string password)
    {
        if (!AuthStore.IsSafeAccountName(account))
        {
            Console.WriteLine("[Auth] 账号为空、过长或包含非法字符（不能包含 / \\ : * ? \" < > |）");
            return 1;
        }

        account = account.Trim();

        AuthServerConfig config = AuthStore.GetServerConfig();
        if (config.Accounts == null)
            config.Accounts = new System.Collections.Generic.List<AuthAccount>();

        bool isNew = !config.Accounts.Any(a =>
            a != null && string.Equals(a.Account, account, StringComparison.OrdinalIgnoreCase));

        AuthAccount target = config.Accounts.FirstOrDefault(a =>
            a != null && string.Equals(a.Account, account, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            target = new AuthAccount
            {
                Account = account,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };
            config.Accounts.Add(target);
        }

        string plainPassword = ResolvePassword(password, "请输入口令（留空自动生成）: ");
        bool generated = string.IsNullOrEmpty(plainPassword);
        if (generated)
            plainPassword = AuthCrypto.GeneratePassword();

        string proof = AuthCrypto.ComputeClientProof(account, plainPassword);
        string salt = AuthCrypto.CreateSalt();

        target.Salt = salt;
        target.Hash = AuthCrypto.HashProof(proof, salt, AuthCrypto.StoreIterations);
        target.Iterations = AuthCrypto.StoreIterations;
        target.Enabled = true;
        target.Account = account;

        AuthStore.SaveServerConfig(config);

        Console.WriteLine();
        Console.WriteLine(isNew ? "已创建账号：" : "已更新账号：");
        Console.WriteLine("  账号: " + account);
        Console.WriteLine("  口令: " + plainPassword + (generated ? "   （自动生成，仅本次显示）" : string.Empty));
        Console.WriteLine();
        Console.WriteLine("请把账号口令告知玩家，并确保配置中的 Enabled 为 true：");
        Console.WriteLine("  " + AuthStore.ServerConfigPath);
        if (!config.Enabled)
            Console.WriteLine("  当前尚未启用认证，可执行: Launcher.exe --auth enable");

        return 0;
    }

    private static int SetPassword(string account, string password)
    {
        if (!AuthStore.IsSafeAccountName(account))
        {
            Console.WriteLine("[Auth] 请指定合法的账号名");
            return 1;
        }

        AuthServerConfig config = AuthStore.GetServerConfig();
        AuthAccount target = config.Accounts.FirstOrDefault(a =>
            a != null && string.Equals(a.Account, account, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            Console.WriteLine("[Auth] 账号不存在: " + account);
            return 1;
        }

        string plainPassword = ResolvePassword(password, "请输入新口令（留空自动生成）: ");
        bool generated = string.IsNullOrEmpty(plainPassword);
        if (generated)
            plainPassword = AuthCrypto.GeneratePassword();

        string proof = AuthCrypto.ComputeClientProof(target.Account, plainPassword);
        string salt = AuthCrypto.CreateSalt();

        target.Salt = salt;
        target.Hash = AuthCrypto.HashProof(proof, salt, AuthCrypto.StoreIterations);
        target.Iterations = AuthCrypto.StoreIterations;

        AuthStore.SaveServerConfig(config);

        Console.WriteLine();
        Console.WriteLine("已重置口令：");
        Console.WriteLine("  账号: " + target.Account);
        Console.WriteLine("  口令: " + plainPassword + (generated ? "   （自动生成，仅本次显示）" : string.Empty));

        return 0;
    }

    private static int RemoveAccount(string account)
    {
        if (!AuthStore.IsSafeAccountName(account))
        {
            Console.WriteLine("[Auth] 请指定合法的账号名");
            return 1;
        }

        AuthServerConfig config = AuthStore.GetServerConfig();
        AuthAccount target = config.Accounts.FirstOrDefault(a =>
            a != null && string.Equals(a.Account, account, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            Console.WriteLine("[Auth] 账号不存在: " + account);
            return 1;
        }

        config.Accounts.Remove(target);
        AuthStore.SaveServerConfig(config);

        Console.WriteLine("[Auth] 已删除账号: " + target.Account);
        return 0;
    }

    private static int ListAccounts()
    {
        AuthServerConfig config = AuthStore.GetServerConfig();

        PrintStatus();

        if (config.Accounts == null || config.Accounts.Count == 0)
        {
            Console.WriteLine("（暂无账号，使用 --auth add <账号> 创建）");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine(Pad("账号", 18) + Pad("状态", 8) + Pad("登录次数", 10) + "最后登录");
        Console.WriteLine(new string('-', 76));

        foreach (AuthAccount account in config.Accounts
            .Where(a => a != null)
            .OrderBy(a => a.Account ?? string.Empty))
        {
            string state = account.Enabled ? "启用" : "禁用";
            string last = string.IsNullOrEmpty(account.LastLoginAt) ? "-" : account.LastLoginAt;

            Console.WriteLine(
                Pad(account.Account, 18) +
                Pad(state, 8) +
                Pad(account.LoginCount.ToString(), 10) +
                last);
        }

        return 0;
    }

    private static int SetEnabled(bool enabled, string account)
    {
        AuthServerConfig config = AuthStore.GetServerConfig();

        if (string.IsNullOrWhiteSpace(account))
        {
            if (enabled && (config.Accounts == null || config.Accounts.Count == 0))
            {
                Console.WriteLine("[Auth] 还没有任何账号，请先执行 --auth add <账号>，否则启用后将无人能登录");
                return 1;
            }

            config.Enabled = enabled;
            AuthStore.SaveServerConfig(config);

            Console.WriteLine("[Auth] 账号认证已" + (enabled ? "启用" : "关闭") + "（下一位玩家登录时即生效）");
            return 0;
        }

        AuthAccount target = config.Accounts.FirstOrDefault(a =>
            a != null && string.Equals(a.Account, account, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            Console.WriteLine("[Auth] 账号不存在: " + account);
            return 1;
        }

        target.Enabled = enabled;
        AuthStore.SaveServerConfig(config);

        Console.WriteLine("[Auth] 账号 " + target.Account + " 已" + (enabled ? "启用" : "禁用"));
        return 0;
    }

    /// <summary>本地验证某个账号的口令是否正确，便于排查“密码明明对却进不去”的问题。</summary>
    private static int VerifyAccount(string account, string password)
    {
        if (!AuthStore.IsSafeAccountName(account))
        {
            Console.WriteLine("[Auth] 请指定合法的账号名");
            return 1;
        }

        string plainPassword = string.IsNullOrEmpty(password)
            ? ReadPassword("请输入待验证的口令: ")
            : password;

        if (string.IsNullOrEmpty(plainPassword))
        {
            Console.WriteLine("[Auth] 未输入口令");
            return 1;
        }

        string proof = AuthCrypto.ComputeClientProof(account, plainPassword);
        string reason;
        bool ok = AuthStore.Diagnose(account, proof, out reason);

        Console.WriteLine();
        Console.WriteLine("  账号: " + account.Trim());
        Console.WriteLine("  结果: " + (ok ? "通过" : "失败") + "（" + reason + "）");

        AuthServerConfig config = AuthStore.GetServerConfig();
        Console.WriteLine("  认证开关: " + (config.Enabled ? "已启用" : "未启用（即使口令正确，服务端也不会校验）"));

        return ok ? 0 : 1;
    }

    private static int PrintStatus()
    {
        AuthServerConfig config = AuthStore.GetServerConfig();

        Console.WriteLine("配置文件: " + AuthStore.ServerConfigPath);
        Console.WriteLine("认证状态: " + (config.Enabled ? "已启用" : "未启用"));
        Console.WriteLine("放行回环: " + (config.AllowLoopback ? "是" : "否"));
        Console.WriteLine("失败锁定: " + (config.MaxFailedAttempts <= 0
            ? "不限制"
            : config.MaxFailedAttempts + " 次 / " + config.LockoutSeconds + " 秒"));
        Console.WriteLine("账号数量: " + (config.Accounts == null ? 0 : config.Accounts.Count));

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Launcher 账号认证管理");
        Console.WriteLine();
        Console.WriteLine("  Launcher.exe --auth add <账号> [口令]      新增账号（口令留空自动生成，\"-\" 交互输入）");
        Console.WriteLine("  Launcher.exe --auth passwd <账号> [口令]   重置口令");
        Console.WriteLine("  Launcher.exe --auth remove <账号>          删除账号");
        Console.WriteLine("  Launcher.exe --auth enable [账号]          启用认证 / 启用单个账号");
        Console.WriteLine("  Launcher.exe --auth disable [账号]         关闭认证 / 禁用单个账号");
        Console.WriteLine("  Launcher.exe --auth list                   查看账号列表");
        Console.WriteLine("  Launcher.exe --auth status                 查看认证状态");
        Console.WriteLine("  Launcher.exe --auth verify <账号> [口令]   本地验证口令是否正确");
        Console.WriteLine();
        Console.WriteLine("账号即游戏昵称；口令只保存派生值，忘记后请用 passwd 重置。");
        Console.WriteLine();
    }

    // ---------------------------------------------------------------- 工具

    private static string ResolvePassword(string argument, string prompt)
    {
        if (string.Equals(argument, "-", StringComparison.Ordinal))
            return ReadPassword(prompt);

        return argument ?? string.Empty;
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);

        StringBuilder builder = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key;
            try
            {
                key = Console.ReadKey(true);
            }
            catch
            {
                Console.WriteLine();
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Enter)
                break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
                Console.Write("*");
            }
        }

        Console.WriteLine();
        return builder.ToString();
    }

    /// <summary>按显示宽度粗略对齐（中文按 2 个字符宽度计算）。</summary>
    private static string Pad(string text, int width)
    {
        text = text ?? string.Empty;

        int displayWidth = 0;
        foreach (char c in text)
            displayWidth += c > 127 ? 2 : 1;

        int padding = width - displayWidth;
        return padding > 0 ? text + new string(' ', padding) : text + " ";
    }
}
