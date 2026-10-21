using System;
using System.Security.Cryptography;
using System.Text;

namespace KartRider;

/// <summary>
/// 账号认证使用的口令派生工具。
///
/// 设计说明：
/// 1. 客户端不会把明文口令发到服务端，而是发送 PBKDF2 派生出的“口令证明”；
/// 2. 服务端只保存 PBKDF2(口令证明, 随机盐)，即使 AuthServer.json 泄露也无法直接得到口令证明；
/// 3. 证明与账号名绑定（盐由账号名派生），因此一份证明无法用于其它账号。
/// </summary>
internal static class AuthCrypto
{
    /// <summary>证明格式版本号，修改派生算法时必须升级。</summary>
    public const string ProofPrefix = "v1";

    /// <summary>客户端派生迭代次数（约 50~120ms，视 CPU 而定）。</summary>
    public const int ClientIterations = 120000;

    /// <summary>服务端存储派生迭代次数。</summary>
    public const int StoreIterations = 120000;

    private const int DerivedKeyLength = 32;

    /// <summary>
    /// 由「账号 + 明文口令」计算口令证明，客户端与服务端校验时使用的格式必须一致。
    /// </summary>
    public static string ComputeClientProof(string account, string password)
    {
        byte[] salt = BuildClientSalt(account);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password ?? string.Empty),
            salt,
            ClientIterations,
            HashAlgorithmName.SHA256,
            DerivedKeyLength);

        return ProofPrefix + "$" + ClientIterations + "$" + Convert.ToBase64String(key);
    }

    /// <summary>
    /// 由账号名派生固定盐：保证不同账号的证明互不通用，同时同一账号在任意客户端结果一致。
    /// </summary>
    public static byte[] BuildClientSalt(string account)
    {
        string normalized = NormalizeAccount(account);
        byte[] raw = Encoding.UTF8.GetBytes("Launcher_V2|Auth|" + normalized);
        return SHA256.HashData(raw);
    }

    /// <summary>账号归一化：去空格 + 转小写，用于派生盐与查表。</summary>
    public static string NormalizeAccount(string account)
    {
        return (account ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>生成服务端随机盐（Base64）。</summary>
    public static string CreateSalt()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>服务端存储用的哈希：PBKDF2(口令证明, 随机盐)。</summary>
    public static string HashProof(string proof, string saltBase64, int iterations)
    {
        byte[] salt = Convert.FromBase64String(saltBase64);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(proof ?? string.Empty),
            salt,
            Math.Max(1000, iterations),
            HashAlgorithmName.SHA256,
            DerivedKeyLength);

        return Convert.ToBase64String(key);
    }

    /// <summary>恒定时间比较，避免通过响应时间推断哈希前缀。</summary>
    public static bool VerifyProof(string proof, string saltBase64, int iterations, string expectedHashBase64)
    {
        try
        {
            byte[] actual = Convert.FromBase64String(HashProof(proof, saltBase64, iterations));
            byte[] expected = Convert.FromBase64String(expectedHashBase64);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>生成随机初始口令（去掉了 0/O/1/l/I 等易混字符）。</summary>
    public static string GeneratePassword(int length = 14)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

        if (length < 6)
            length = 6;

        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        return new string(chars);
    }
}
