using Profile;
using System;
using System.IO;
using System.Text;

namespace KartRider;

/// <summary>
/// 认证日志。
///
/// 同时输出到控制台和 Profile/Auth.log：
/// 控制台默认是隐藏的，出问题时可直接看 Auth.log 定位
/// （服务端有没有挂载、每次登录是放行还是拒绝、原因是什么）。
/// </summary>
internal static class AuthLog
{
    private static readonly object SyncRoot = new object();
    private const long MaxSize = 2 * 1024 * 1024;

    public static string FilePath
    {
        get { return Path.Combine(FileName.ProfileDir, "Auth.log"); }
    }

    public static void Write(string message)
    {
        try
        {
            Console.WriteLine("[Auth] " + message);
        }
        catch
        {
        }

        try
        {
            lock (SyncRoot)
            {
                string directory = FileName.ProfileDir;
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                FileInfo info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxSize)
                    File.Move(FilePath, FilePath + ".old", true);

                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
                File.AppendAllText(FilePath, line, new UTF8Encoding(false));
            }
        }
        catch
        {
        }
    }
}
