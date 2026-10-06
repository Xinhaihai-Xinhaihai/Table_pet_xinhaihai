using System.Diagnostics;
using System.IO;

namespace XinHaiHai;

public class MusicLaunch
{
    public string Name;             // 播放器名(用于台词/日志)
    public int PlayDelaySeconds;    // 几秒后按“播放”媒体键
    public bool CanAutoPlay;        // 网页版无法自动播放
}

/// <summary>
/// 没人陪的时候,自己打开听歌软件并真正开始放歌:
/// 1) 若播放器已在运行 → 直接按媒体“播放”键;
/// 2) 否则按 用户配置 → 常见播放器 顺序启动,等它加载完再按“播放”键;
/// 3) 都没有 → 打开网页版(浏览器不允许自动播放,只能到这一步)。
/// </summary>
public static class MusicLauncher
{
    static readonly (string proc, string name)[] KnownPlayers =
    {
        ("cloudmusic", "网易云音乐"),
        ("QQMusic",    "QQ音乐"),
        ("KuGou",      "酷狗音乐"),
        ("KwMusic",    "酷我音乐"),
        ("Spotify",    "Spotify"),
        ("wmplayer",   "Windows 播放器"),
        ("foobar2000", "foobar2000"),
        ("AIMP",       "AIMP"),
        ("PotPlayerMini64", "PotPlayer"),
        ("PotPlayer64", "PotPlayer"),
    };

    /// <summary>检测使用者是否有听歌软件正在运行(用于“听歌模式”)。返回播放器名或 null。</summary>
    public static string DetectRunningPlayer()
    {
        foreach (var (proc, name) in KnownPlayers)
        {
            try { if (Process.GetProcessesByName(proc).Length > 0) return name; }
            catch { }
        }
        return null;
    }

    public static MusicLaunch Launch()
    {
        // 0) 已经开着的播放器:不用再启动,直接让它放歌
        foreach (var (proc, name) in KnownPlayers)
        {
            try
            {
                if (Process.GetProcessesByName(proc).Length > 0)
                    return new MusicLaunch { Name = name, PlayDelaySeconds = 2, CanAutoPlay = true };
            }
            catch { }
        }

        int coldDelay = Math.Clamp(Store.Config.musicPlayDelaySeconds, 3, 120);
        var candidates = new List<(string path, string name)>();

        string custom = Store.Config.musicCommand;
        if (!string.IsNullOrWhiteSpace(custom)) candidates.Add((custom, "主人指定的播放器"));

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        candidates.Add((Path.Combine(local, @"Netease\CloudMusic\cloudmusic.exe"), "网易云音乐"));
        candidates.Add((Path.Combine(pf86, @"Netease\CloudMusic\cloudmusic.exe"), "网易云音乐"));
        candidates.Add((Path.Combine(pf86, @"Tencent\QQMusic\QQMusic.exe"), "QQ音乐"));
        candidates.Add((Path.Combine(pf86, @"KuGou\KGMusic\KuGou.exe"), "酷狗音乐"));
        candidates.Add((Path.Combine(pf86, @"Kuwo\KwMusic\KwMusic.exe"), "酷我音乐"));
        candidates.Add((Path.Combine(roam, @"Spotify\Spotify.exe"), "Spotify"));
        candidates.Add((Path.Combine(pf, @"Windows Media Player\wmplayer.exe"), "Windows 播放器"));

        foreach (var (path, name) in candidates)
        {
            try
            {
                if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return new MusicLaunch { Name = name, CanAutoPlay = false };
                }
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return new MusicLaunch { Name = name, PlayDelaySeconds = coldDelay, CanAutoPlay = true };
                }
            }
            catch { }
        }

        try
        {
            Process.Start(new ProcessStartInfo("https://music.163.com") { UseShellExecute = true });
            return new MusicLaunch { Name = "网页版网易云音乐", CanAutoPlay = false };
        }
        catch { return null; }
    }
}
