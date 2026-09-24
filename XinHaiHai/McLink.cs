using System.Diagnostics;
using System.IO;
using System.Text;

namespace XinHaiHai;

/// <summary>
/// 桌宠进 MC。原版、Fabric、Forge、NeoForge 登录协议相同，不装模组。
/// 游戏名只能用英文 XinHaiHai，气泡里仍叫心海海。
/// </summary>
public sealed class McLink : IDisposable
{
    public const string GameName = "XinHaiHai";

    Process proc;
    readonly StringBuilder buf = new();
    readonly object gate = new();

    public bool Running => proc != null && !proc.HasExited;
    public event Action<string> Chat;     // 游戏里别人说的话
    public event Action<string> Status;   // 连上 / 断开 / 报错
    public event Action Death;            // 桌宠在游戏里死了

    public void Start()
    {
        lock (gate)
        {
            if (Running) return;
            string root = AppContext.BaseDirectory;
            string script = Path.Combine(root, "mc", "bot.js");
            string node = FindNode();
            if (node == null || !File.Exists(script))
            {
                Status?.Invoke("进服用的程序没找到");
                return;
            }
            var psi = new ProcessStartInfo(node, "\"" + script + "\"")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false),
            };
            psi.Environment["MC_HOST"] = string.IsNullOrWhiteSpace(Store.Config.mcHost) ? "127.0.0.1" : Store.Config.mcHost.Trim();
            psi.Environment["MC_PORT"] = (Store.Config.mcPort <= 0 ? 25565 : Store.Config.mcPort).ToString();
            psi.Environment["MC_NAME"] = GameName;
            try
            {
                proc = Process.Start(psi);
                proc.OutputDataReceived += OnLine;
                proc.BeginOutputReadLine();
                proc.EnableRaisingEvents = true;
                proc.Exited += (s, e) => Status?.Invoke("和服务器断开了");
            }
            catch (Exception ex)
            {
                Status?.Invoke("进服失败:" + ex.Message);
                proc = null;
            }
        }
    }

    public void Say(string text)
    {
        if (!Running || string.IsNullOrWhiteSpace(text)) return;
        try { proc.StandardInput.WriteLine(text.Replace("\r", " ").Replace("\n", " ")); }
        catch { }
    }

    public void Stop()
    {
        lock (gate)
        {
            var p = proc;
            proc = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(true); } catch { }
            try { p.Dispose(); } catch { }
        }
    }

    public void Dispose() => Stop();

    void OnLine(object s, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        string line = e.Data;
        if (line.StartsWith("SPAWN ")) Status?.Invoke("吾进游戏啦");
        else if (line.StartsWith("KICK ") || line.StartsWith("ERR ")) Status?.Invoke(Clean(line));
        else if (line.StartsWith("EVENT death")) { try { Death?.Invoke(); } catch { } }
        else if (line.StartsWith("MSG "))
        {
            string msg = line.Substring(4).Trim();
            if (msg.Contains("<" + GameName + ">")) return;
            if (msg.Contains(GameName) && (msg.Contains("joined") || msg.Contains("left"))) return;
            Chat?.Invoke(msg);
        }
    }

    static string Clean(string line)
    {
        if (line.Contains("ECONNREFUSED") || line.Contains("connect E")) return "服务器没开，25565 连不上";
        if (line.Length > 40) return line[..40];
        return line;
    }

    static string FindNode()
    {
        // 1. 优先从 mc/ 目录找(用户把 node.exe 放这里最方便)
        string mcDir = Path.Combine(AppContext.BaseDirectory, "mc");
        string mcNode = Path.Combine(mcDir, "node.exe");
        if (File.Exists(mcNode)) return mcNode;

        // 2. 系统 PATH
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            try
            {
                string exe = Path.Combine(dir.Trim(), "node.exe");
                if (File.Exists(exe)) return exe;
            }
            catch { }
        }

        // 3. 常见安装位置
        string[] common = {
            @"C:\Program Files
odejs
ode.exe",
            @"C:\Program Files (x86)
odejs
ode.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs
ode
ode.exe"),
        };
        foreach (string c in common)
            if (File.Exists(c)) return c;

        return null;
    }
}
