using Profile;
using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace KartRider;

/// <summary>
/// 客户端侧认证：弹出登录框、缓存凭据，并把口令证明写入 passport。
///
/// 只有当服务端启用认证时才需要走这一步：
/// - 若 Profile/AuthClient.json 的 Enabled=false，直接跳过；
/// - 若当前连接的是本机服务器，且本机 AuthServer.json 未启用认证（或允许回环），也直接跳过。
/// </summary>
internal static class AuthClient
{
    private static readonly object SyncRoot = new object();

    private static string _cachedAccount;
    private static string _cachedProof;
    private static int _prompting;

    /// <summary>
    /// 为 passport 填充认证信息。
    /// 返回 false 表示用户取消登录，调用方应当终止启动流程。
    /// </summary>
    public static bool FillAuth(DataPacket packet)
    {
        if (packet == null)
            return true;

        AuthClientConfig config;
        try
        {
            config = AuthStore.GetClientConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Auth] 读取客户端认证配置失败: " + ex.Message);
            config = new AuthClientConfig();
        }

        if (!config.Enabled)
            return true;

        if (!ShouldPrompt())
            return true;

        // 同一进程内复用上次输入的凭据，避免每次点击“启动游戏”都弹窗
        lock (SyncRoot)
        {
            if (_cachedProof != null)
            {
                packet.Account = _cachedAccount;
                packet.AuthProof = _cachedProof;
                return true;
            }
        }

        if (Interlocked.CompareExchange(ref _prompting, 1, 0) != 0)
        {
            Console.WriteLine("[Auth] 登录窗口已打开，忽略本次启动请求");
            return false;
        }

        try
        {
            AuthLoginResult result = PromptOnUiThread(config);

            if (result == null || result.Cancelled)
            {
                Console.WriteLine("[Auth] 用户取消登录，已停止启动游戏");
                return false;
            }

            if (result.Skip)
            {
                Console.WriteLine("[Auth] 已跳过账号认证");
                return true;
            }

            string account = (result.Account ?? string.Empty).Trim();

            // 账号只用于登录校验；游戏昵称仍取自「设置」，两者互不影响
            packet.Account = account;
            packet.AuthProof = result.Proof;

            lock (SyncRoot)
            {
                _cachedAccount = account;
                _cachedProof = result.Proof;
            }

            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _prompting, 0);
        }
    }

    // ---------------------------------------------------------------- 内部实现

    /// <summary>判断是否需要弹登录框。</summary>
    private static bool ShouldPrompt()
    {
        try
        {
            string serverIp = ProfileService.SettingConfig == null
                ? null
                : ProfileService.SettingConfig.ServerIP;

            if (!IsLocalServer(serverIp))
                return true;

            // 本机服务器：直接读同目录的 AuthServer.json，未启用认证就不打扰玩家
            AuthServerConfig serverConfig = AuthStore.TryReadServerConfigSilently();
            if (serverConfig == null)
                return true;

            if (!serverConfig.Enabled)
                return false;

            if (serverConfig.AllowLoopback && IsLoopback(serverIp))
                return false;

            return true;
        }
        catch
        {
            return true;
        }
    }

    private static AuthLoginResult PromptOnUiThread(AuthClientConfig config)
    {
        Launcher owner = Program.LauncherDlg;

        if (owner != null && owner.IsHandleCreated && owner.InvokeRequired)
        {
            AuthLoginResult result = null;
            try
            {
                owner.Invoke(new Action(() => { result = ShowDialogCore(owner, config); }));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Auth] 显示登录窗口失败: " + ex.Message);
                return null;
            }
            return result;
        }

        return ShowDialogCore(owner, config);
    }

    private static AuthLoginResult ShowDialogCore(Launcher owner, AuthClientConfig config)
    {
        using (AuthLoginForm form = new AuthLoginForm(config.LastAccount))
        {
            DialogResult dialogResult = (owner != null && owner.Visible)
                ? form.ShowDialog(owner)
                : form.ShowDialog();

            if (dialogResult != DialogResult.OK)
                return new AuthLoginResult { Cancelled = true };

            if (form.SkipAuth)
                return new AuthLoginResult { Skip = true };

            string account = form.Account;
            string proof;

            try
            {
                proof = AuthCrypto.ComputeClientProof(account, form.Password);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "生成认证信息失败：" + ex.Message,
                    "账号登录",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return new AuthLoginResult { Cancelled = true };
            }

            config.LastAccount = form.RememberAccount ? account : string.Empty;
            config.RememberAccount = form.RememberAccount;
            AuthStore.SaveClientConfig(config);

            return new AuthLoginResult
            {
                Account = account,
                Proof = proof,
            };
        }
    }

    private static bool IsLocalServer(string serverIp)
    {
        if (string.IsNullOrWhiteSpace(serverIp))
            return false;

        string ip = serverIp.Trim();

        if (string.Equals(ip, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        IPAddress address;
        if (IPAddress.TryParse(ip, out address))
            return IPAddress.IsLoopback(address);

        return false;
    }

    private static bool IsLoopback(string serverIp)
    {
        if (string.IsNullOrWhiteSpace(serverIp))
            return false;

        string ip = serverIp.Trim();
        if (string.Equals(ip, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        IPAddress address;
        if (IPAddress.TryParse(ip, out address))
            return IPAddress.IsLoopback(address);

        return false;
    }
}
