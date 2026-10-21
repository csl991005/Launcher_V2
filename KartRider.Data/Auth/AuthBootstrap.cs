using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace KartRider;

/// <summary>
/// 认证模块的挂载入口。
///
/// 使用 [ModuleInitializer] 在程序集加载、Main 执行之前完成注册，
/// 这样无需修改 Program.cs / ClientSession.cs，上游同步时不会产生冲突。
/// </summary>
internal static class AuthBootstrap
{
    private static int _initialized;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        string[] args = Environment.GetCommandLineArgs();

        // 命令行管理入口：Launcher.exe --auth ...
        try
        {
            if (AuthCli.TryRun(args))
                return;
        }
        catch (Exception ex)
        {
            AuthLog.Write("命令行处理失败: " + ex.Message);
        }

        try
        {
            // 仅在正常启动时生成默认配置；rho/xml 等工具模式不写文件
            if (!IsToolMode(args))
                AuthStore.EnsureServerConfig();

            PacketDispatcher.RegisterHandler(new AuthPacketHandler());
            AuthLog.Write("登录拦截器已注册（PqCnAuthenLogin / PqLogin）");

            AuthUiHook.Install();

            AuthServerConfig config = AuthStore.GetServerConfig();
            int usable = 0;
            int defaultPassword = 0;
            if (config.Accounts != null)
            {
                for (int i = 0; i < config.Accounts.Count; i++)
                {
                    AuthAccount account = config.Accounts[i];
                    if (account == null || !account.Enabled)
                        continue;

                    usable++;
                    if (account.DefaultPassword)
                        defaultPassword++;
                }
            }

            if (!config.Enabled)
                AuthLog.Write("账号认证未启用：任何账号密码都能登录（主界面「账号管理」可开启）");
            else if (usable == 0)
                AuthLog.Write("警告：认证已启用但没有任何可用账号，所有玩家都会被拒绝");
            else
                AuthLog.Write("账号认证已启用，可用账号 " + usable + " 个");

            if (defaultPassword > 0)
                AuthLog.Write("提醒：仍有账号使用初始口令（" + AuthStore.DefaultAccountName + " / " +
                              AuthStore.DefaultAccountPassword + "），请尽快在「账号管理」中修改");
        }
        catch (Exception ex)
        {
            AuthLog.Write("模块挂载失败: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>带参数运行（Rho 打包/解包等工具模式）时不创建配置文件。</summary>
    private static bool IsToolMode(string[] args)
    {
        if (args == null || args.Length <= 1)
            return false;

        // 自更新后的重启属于正常启动
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "/updated", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
