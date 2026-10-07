using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XinHaiHai;

/// <summary>
/// 我的世界(MC)联机面板:连接本机/局域网/穿透/云服务器、扫描局域网、
/// 安装 mc 模块(npm)、node 路径、游戏名设置、聊天框、大模型代答。
/// 从 MainWindow 拆出,行为不变。
/// </summary>
public partial class MainWindow
{
    void StartMc(string host, int port)
    {
        if (mc.Running)
        {
            mc.Stop();
            ShowBubble("先断开,重新连接~", 3);
        }
        Store.Config.mcHost = host;
        Store.Config.mcPort = port;
        Store.SaveConfig();
        ShowBubble("吾去游戏里找主人~", 4);
        mc.Start();
    }

    void ShowLanConnect()
    {
        var win = new Window
        {
            Title = "连接局域网",
            Width = 400, Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e)),
            Foreground = Brushes.White,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
        };
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(new TextBlock { Text = "正在扫描局域网 MC 服务器…", Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });

        var lb = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            Foreground = Brushes.White,
            Height = 150,
            Margin = new Thickness(0, 0, 0, 8),
        };
        sp.Children.Add(lb);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnManual = new Button { Content = "手动输入IP", Width = 100, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)), Foreground = Brushes.White };
        var btnConn = new Button { Content = "连接", Width = 80, Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)), Foreground = Brushes.White };
        btnConn.Click += (s, e) =>
        {
            if (lb.SelectedItem is string selected)
            {
                win.Close();
                StartMc(selected.Trim(), 25565);
            }
        };
        btnManual.Click += (s, e) => { win.Close(); ShowCloudConnect(); };
        row.Children.Add(btnManual);
        row.Children.Add(btnConn);
        sp.Children.Add(row);
        win.Content = sp;
        win.Show();

        _ = ScanLanAsync(lb, sp.Children[0] as TextBlock);
    }

    async Task ScanLanAsync(ListBox lb, TextBlock status)
    {
        var clients = new System.Net.NetworkInformation.Ping();
        var subnets = new List<string>();

        // get local IP segments
        try
        {
            var host = System.Net.Dns.GetHostName();
            var ips = System.Net.Dns.GetHostEntry(host).AddressList;
            foreach (var ip in ips)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var bytes = ip.GetAddressBytes();
                    if (bytes[0] == 192 && bytes[1] == 168)
                        subnets.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}");
                    else if (bytes[0] == 10)
                        subnets.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}");
                    else if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                        subnets.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}");
                }
            }
        }
        catch { }

        if (subnets.Count == 0) { Dispatcher.BeginInvoke(() => status.Text = "未检测到局域网"); return; }

        status.Text = $"正在扫描 {subnets[0]}.x …";
        var found = new List<(string ip, int port)>();
        var tasks = new List<Task>();
        var sem = new System.Threading.SemaphoreSlim(50);

        foreach (var subnet in subnets)
        {
            for (int i = 1; i <= 254; i++)
            {
                string ip = $"{subnet}.{i}";
                tasks.Add(Task.Run(async () =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        var reply = await clients.SendPingAsync(ip, 200);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                        {
                            // try common MC ports
                            foreach (int port in new[] { 25565, 25575, 25566 })
                            {
                                try
                                {
                                    using var tcp = new System.Net.Sockets.TcpClient();
                                    var connectTask = tcp.ConnectAsync(ip, port);
                                    if (await Task.WhenAny(connectTask, Task.Delay(300)) == connectTask && tcp.Connected)
                                    {
                                        lock (found) found.Add((ip, port));
                                        Dispatcher.BeginInvoke(() => lb.Items.Add(ip));
                                        tcp.Close();
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                    finally { sem.Release(); }
                }));
            }
        }

        await Task.WhenAll(tasks);
        Dispatcher.BeginInvoke(() =>
            status.Text = found.Count > 0
                ? $"找到 {found.Count} 台服务器,选一台点连接"
                : "未发现 MC 服务器,可以点「手动输入」");
    }

    void ShowCloudConnect()
    {
        var win = new Window
        {
            Title = "连接云服务器",
            Width = 360, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e)),
            Foreground = Brushes.White,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
        };
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(new TextBlock { Text = "服务器 IP 地址:", Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
        var tbHost = new TextBox
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            FontSize = 14, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8),
            Text = Store.Config.mcHost,
        };
        sp.Children.Add(tbHost);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btn = new Button
        {
            Content = "连接", Width = 80,
            Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            Foreground = Brushes.White, FontSize = 14,
        };
        void Go()
        {
            string host = tbHost.Text.Trim();
            if (!string.IsNullOrWhiteSpace(host))
            {
                win.Close();
                StartMc(host, 25565);
            }
        }
        btn.Click += (s, e) => Go();
        tbHost.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) Go(); };
        row.Children.Add(btn);
        sp.Children.Add(row);
        win.Content = sp;
        win.Show();
        tbHost.Focus();
    }

    void ShowMcNameSetup()
    {
        string current = Store.Config.mcGameName;
        var win = new Window
        {
            Title = "MC 游戏名(只能英文)",
            Width = 360, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e)),
            Foreground = Brushes.White,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
        };
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(new TextBlock { Text = "游戏内显示的名字(只能英文,如 XinHaiHai):", Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
        var tb = new TextBox
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            FontSize = 14, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8),
            Text = current,
        };
        sp.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btn = new Button { Content = "保存", Width = 80, Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)), Foreground = Brushes.White };
        btn.Click += (s, e) =>
        {
            string val = tb.Text.Trim();
            if (string.IsNullOrWhiteSpace(val)) { ShowBubble("名字不能为空", 3); return; }
            if (!System.Text.RegularExpressions.Regex.IsMatch(val, @"^[a-zA-Z0-9_]+$")) { ShowBubble("只能填英文和数字", 3); return; }
            Store.Config.mcGameName = val;
            Store.SaveConfig();
            ShowBubble("MC游戏名已改为:" + val, 4);
            win.Close();
        };
        tb.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
        row.Children.Add(btn);
        sp.Children.Add(row);
        win.Content = sp;
        win.Show();
        tb.Focus();
    }

    void ShowNodePathSetup()
    {
        string current = Store.Config.nodePath;
        string mcDir = Path.Combine(AppContext.BaseDirectory, "mc");
        string hint = string.IsNullOrWhiteSpace(current) ? $"留空=自动查找\n推荐:把 node.exe 复制到 {mcDir}" : $"当前:{current}";

        var win = new Window
        {
            Title = "设置 node.exe 路径",
            Width = 420, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e)),
            Foreground = Brushes.White,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
        };
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var tb = new TextBox
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            FontSize = 13, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8),
            Text = current,
        };
        sp.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnClear = new Button { Content = "恢复自动", Width = 80, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)), Foreground = Brushes.White };
        var btnSave = new Button { Content = "保存", Width = 80, Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)), Foreground = Brushes.White };
        btnClear.Click += (s, e) => { Store.Config.nodePath = ""; Store.SaveConfig(); ShowBubble("已恢复自动查找", 3); win.Close(); };
        btnSave.Click += (s, e) =>
        {
            string val = tb.Text.Trim();
            if (!string.IsNullOrWhiteSpace(val) && !File.Exists(val)) { ShowBubble("文件不存在", 3); return; }
            Store.Config.nodePath = val;
            Store.SaveConfig();
            ShowBubble("已保存", 3);
            win.Close();
        };
        row.Children.Add(btnClear);
        row.Children.Add(btnSave);
        sp.Children.Add(row);
        win.Content = sp;
        win.Show();
        tb.Focus();
    }

    void InstallMcModule()
    {
        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
        { ShowBubble("需要网络才能安装", 4); return; }
        ShowBubble("正在安装MC模块,请稍候…", 8);
        string mcDir = Path.Combine(AppContext.BaseDirectory, "mc");
        // 后台线程跑 npm install,避免 UI 线程同步等待导致卡死
        Task.Run(async () =>
        {
            string output = "", err = "";
            int exitCode = -1;
            bool started = false;
            try
            {
                if (!Directory.Exists(mcDir))
                {
                    err = "mc 目录不存在(发布包缺失 mc/bot.js 模板)";
                }
                else
                {
                    var psi = new ProcessStartInfo("npm", "install --no-audit --no-fund")
                    {
                        WorkingDirectory = mcDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    };
                    var p = Process.Start(psi);
                    started = true;
                    var oTask = Task.Run(() => p.StandardOutput.ReadToEnd());
                    var eTask = Task.Run(() => p.StandardError.ReadToEnd());
                    bool exited = await Task.Run(() => p.WaitForExit(180000));
                    output = await oTask;
                    err = await eTask;
                    if (!exited)
                    {
                        try { p.Kill(true); } catch { }
                        err = "安装超时(3分钟)";
                    }
                    else
                    {
                        exitCode = p.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
                if (!started)
                {
                    Dispatcher.BeginInvoke(() => ShowBubble("npm没装好,请先装Node.js", 8));
                    return;
                }
            }
            Dispatcher.BeginInvoke(() =>
            {
                if (started && exitCode == 0) ShowBubble("MC模块安装成功!", 5);
                else ShowBubble("安装失败:" + (err.Length > 80 ? err[..80] : err), 8);
            });
        });
    }

    void ShowFrpConnect()
    {
        var win = new Window
        {
            Title = "局域网/内网穿透",
            Width = 360, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e)),
            Foreground = Brushes.White,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
        };
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(new TextBlock { Text = "服务器地址(IP 或域名):", Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
        var tbHost = new TextBox
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            FontSize = 14, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8),
            Text = Store.Config.mcHost,
        };
        sp.Children.Add(tbHost);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btn = new Button
        {
            Content = "连接", Width = 80,
            Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            Foreground = Brushes.White, FontSize = 14,
        };
        void Go()
        {
            string host = tbHost.Text.Trim();
            if (!string.IsNullOrWhiteSpace(host))
            {
                win.Close();
                StartMc(host, 25565);
            }
        }
        btn.Click += (s, e) => Go();
        tbHost.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) Go(); };
        row.Children.Add(btn);
        sp.Children.Add(row);
        win.Content = sp;
        win.Show();
        tbHost.Focus();
    }

    void OnMcStatus(string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowBubble(text, 5);
            if (text == "吾进游戏啦") mc.Say("/op " + McLink.GameName);
        });
    }

    void OnMcDeath()
    {
        Dispatcher.BeginInvoke(() => ShowBubble("呜呜吾在游戏里挂了…", 5));
    }

    void OnMcChat(string msg)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowBubble(msg.Length > 40 ? msg[..40] : msg, 8);
            _ = ReplyInGame(msg);
        });
    }

    async Task ReplyInGame(string msg)
    {
        string body = msg;
        int a = msg.LastIndexOf('<'), b = msg.LastIndexOf('>');
        string who = "主人";
        if (a >= 0 && b > a) { who = msg.Substring(a + 1, b - a - 1); body = msg[(b + 1)..].Trim(); }
        if (who == McLink.GameName || string.IsNullOrWhiteSpace(body)) return;

        string reply = null;
        string cmd = null;
        if (LlmClient.Enabled)
        {
            string raw = await LlmClient.AskAsync(
                "你在我的世界服务器里,已经是管理员。下面这句话来自玩家「" + who + "」:" + body + "\n" +
                "如果这句话是在让你做事(传送、给物品、调时间、调天气、调模式等),只输出一条原版指令,以 / 开头,不要解释。\n" +
                "传送某人到某处用 /tp 玩家名 目的地。玩家没说自己的游戏名时,目的地按他说的写。\n" +
                "如果只是聊天,就用人设回一句中文,40字以内,不要以 / 开头。");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                raw = raw.Trim();
                if (raw.StartsWith("/")) cmd = raw.Split('\n')[0].Trim();
                else reply = raw;
            }
        }
        if (cmd != null)
        {
            mc.Say(cmd);
            Dispatcher.BeginInvoke(() => ShowBubble("好,吾去办:" + cmd, 5));
            return;
        }
        if (string.IsNullOrWhiteSpace(reply)) reply = Dialogue.Chat;
        reply = reply.Replace("\r", " ").Replace("\n", " ").Trim();
        if (reply.Length > 80) reply = reply[..80];
        mc.Say(reply);
        Dispatcher.BeginInvoke(() => ShowBubble(reply, 5));
    }

    void ShowMcChatBox()
    {
        if (!mc.Running) { ShowBubble("先点「进入我的世界」", 4); return; }
        var win = new Window
        {
            Title = "MC 聊天",
            Width = 340, Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e)),
            Foreground = Brushes.White,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
        };
        var sp = new StackPanel { Margin = new Thickness(10) };
        var tb = new TextBox
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            FontSize = 14,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 8),
            AcceptsReturn = false,
            MaxLength = 256,
        };
        var btn = new Button
        {
            Content = "发送",
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            Foreground = Brushes.White,
        };
        void Send()
        {
            if (!string.IsNullOrWhiteSpace(tb.Text))
            {
                mc.Say(tb.Text.Trim());
                ShowBubble("已发送:" + tb.Text.Trim(), 4);
            }
            win.Close();
        }
        btn.Click += (s, e) => Send();
        tb.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) Send(); };
        sp.Children.Add(tb);
        sp.Children.Add(btn);
        win.Content = sp;
        win.Show();
        tb.Focus();
    }


    void ExitApp()
    {
        mc.Stop();
        Persist();
        Store.Log("========== 心海海 退出 ==========");
        mischief.Stop();
        try { tray.Visible = false; tray.Dispose(); } catch { }
        Application.Current.Shutdown();
    }
}
