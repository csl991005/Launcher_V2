using System;
using System.Collections.Generic;

namespace KartRider;

/// <summary>
/// 服务端认证配置，保存在 Profile/AuthServer.json。
/// 该文件由 AuthStore 自动创建，可手工编辑（支持注释与尾逗号）。
/// </summary>
public sealed class AuthServerConfig
{
    /// <summary>总开关。false 时完全保持原版行为，不做任何登录校验。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否放行本机回环连接（127.0.0.1 / ::1）。
    /// 注意：如果服务器通过 frp / natapp 等隧道暴露，远端连接的源地址也会变成回环，
    /// 此时开启本项等同于放行所有玩家，请谨慎。
    /// </summary>
    public bool AllowLoopback { get; set; }

    /// <summary>允许的连续失败次数，超过后临时锁定。&lt;= 0 表示不限制。</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>锁定时长（秒）。</summary>
    public int LockoutSeconds { get; set; } = 300;

    /// <summary>是否在控制台输出成功登录记录。</summary>
    public bool LogSuccessfulLogin { get; set; } = true;

    /// <summary>账号列表。</summary>
    public List<AuthAccount> Accounts { get; set; } = new List<AuthAccount>();
}

/// <summary>单个账号。口令只保存派生值，无法还原。</summary>
public sealed class AuthAccount
{
    /// <summary>登录账号，与游戏昵称无关。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>
    /// 绑定的游戏昵称（可选）。
    /// 留空表示由玩家在「设置」里自由填写昵称；填写后，客户端提交的昵称必须与此一致。
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>服务端随机盐（Base64）。</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>PBKDF2(口令证明, Salt) 的结果（Base64）。</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>存储哈希使用的迭代次数。</summary>
    public int Iterations { get; set; } = AuthCrypto.StoreIterations;

    /// <summary>是否允许登录。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>是否仍在使用初始默认口令（admin/admin），用于提醒管理员修改。</summary>
    public bool DefaultPassword { get; set; }

    /// <summary>备注（例如使用者姓名）。</summary>
    public string Note { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public string LastLoginAt { get; set; } = string.Empty;

    public string LastLoginIp { get; set; } = string.Empty;

    public int LoginCount { get; set; }
}

/// <summary>
/// 客户端认证配置，保存在 Profile/AuthClient.json。
/// Enabled=false 时启动器不再弹出登录框（适用于未启用认证的服务器）。
/// </summary>
public sealed class AuthClientConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>上次使用的账号，用于预填输入框。</summary>
    public string LastAccount { get; set; } = string.Empty;

    /// <summary>是否记住账号名（不保存口令）。</summary>
    public bool RememberAccount { get; set; } = true;
}

/// <summary>连接级的认证状态，挂在 SessionGroup 上，不参与序列化。</summary>
internal sealed class AuthSessionState
{
    public bool Authenticated;
    public string Account = string.Empty;
    public string Ip = string.Empty;
}

/// <summary>登录框返回结果。</summary>
internal sealed class AuthLoginResult
{
    public bool Cancelled;
    public bool Skip;
    public string Account = string.Empty;
    public string Proof = string.Empty;
}
