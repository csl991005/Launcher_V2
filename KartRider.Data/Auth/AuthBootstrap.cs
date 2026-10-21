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
            SafeLog("[Auth] 命令行处理失败: " + ex.Message);
        }

        try
        {
            // 仅在正常启动时生成默认配置；rho/xml 等工具模式不写文件
            if (!IsToolMode(args))
                AuthStore.EnsureServerConfig();

            PacketDispatcher.RegisterHandler(new AuthPacketHandler());

            AuthServerConfig config = AuthStore.GetServerConfig();
            SafeLog("[Auth] 账号认证模块已挂载：" +
                    (config.Enabled ? "已启用" : "未启用（执行 Launcher.exe --auth enable 开启）"));
        }
        catch (Exception ex)
        {
            SafeLog("[Auth] 模块挂载失败: " + ex.Message);
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

    private static void SafeLog(string message)
    {
        try
        {
            Console.WriteLine(message);
        }
        catch
        {
        }
    }
}
