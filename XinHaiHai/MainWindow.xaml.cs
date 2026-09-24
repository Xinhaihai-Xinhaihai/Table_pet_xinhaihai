using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace XinHaiHai;

public partial class MainWindow : Window
{
    readonly PetState state = new();
    readonly DailyPlanner planner = new();
    readonly MouseMischief mischief = new();
    readonly Random rnd = new();
    readonly Stopwatch clock = Stopwatch.StartNew();

    readonly DispatcherTimer mainTick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer fastTick = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly DispatcherTimer topTick = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer blinkTick = new();
    readonly DispatcherTimer bubbleHide = new();
    readonly DispatcherTimer saveTick = new() { Interval = TimeSpan.FromSeconds(30) };

    WF.NotifyIcon tray;
    ContextMenu petMenu;
    MenuItem miHead, miPat, miPlay, miChat, miShop, miStatus, miFood, miDrink, miPlan, miReplan, miHide, miDir, miExit;
    MenuItem miLook, miLookOfficial, miLookCustom, miLookGif, miLookLive2d, miRename;
    MenuItem miTalk, miTalkLocal, miTalkLlm, miLlmSetup;
    MenuItem miMc, miMcLocal, miMcLan, miMcFrp, miMcCloud, miMcChat, miMcInstall, miMcNode, miMcName;
    readonly McLink mc = new();
    Window planWin, shopWin, pantryWin;

    // 金币与工资
    int coins;
    readonly HashSet<string> paidKeys = new();

    // 自主走动
    bool walking;
    Point walkTarget;
    double walkSpeed;

    // 音乐播放控制
    DispatcherTimer playTimer;
    bool petMusicPlaying;

    // 听歌模式(检测到使用者在放歌)
    bool listenMode;
    bool wasSelfListening;
    int audioHitStreak, audioMissStreak;
    DateTime nextAudioCheck = DateTime.MinValue;
    DateTime nextAutoEatAt = DateTime.MinValue;
    Window statusWin;

    DateTime lastTick = DateTime.Now;
    DateTime nextSassyAt = DateTime.Now.AddSeconds(90);
    DateTime happyUntil = DateTime.MinValue;
    DateTime mischiefEpisodeEnd = DateTime.MinValue;
    DateTime mischiefCooldownUntil = DateTime.Now.AddMinutes(10);   // 启动宽限
    bool sleeping, hidden, firstRun, posRestored;
    string taskNow;
    ScheduledTask curTask;
    Mood faceNow = (Mood)(-1);

    // 拖拽
    bool dragging; double dragTotal;
    Point dragStartCursor, dragStartWin;

    // 皮肤
    Dictionary<string, BitmapImage> skins;
    Dictionary<string, GifAnimation> gifSkins;
    DispatcherTimer gifTimer;
    int gifFrameIndex;
    GifAnimation curGifAnim;

    string skinMode = "official";            // official / custom / live2d(运行时已解析,不含 auto)
    Microsoft.Web.WebView2.Wpf.WebView2 l2d; // Live2D 宿主
    bool l2dReady;

    string PetName => Store.Config.petName;

    bool Paused => sleeping || taskNow != null || hidden;

    public MainWindow()
    {
        InitializeComponent();
        LoadStateAndDebug();
        LoadSkins(); LoadGifs();
        PlaceWindow();
        SetupTray();
        SetupMenu();

        MouseLeftButtonDown += OnLBDown;
        MouseMove += OnMMove;
        MouseLeftButtonUp += OnLBUp;
        LostMouseCapture += (s, e) => dragging = false;

        SourceInitialized += (s, e) => Native.SetClickThrough(this, false);
        Loaded += OnLoadedOnce;
        Closing += (s, e) => { mc.Stop(); Persist(); };
        mc.Status += OnMcStatus;
        mc.Chat += OnMcChat;
        mc.Death += OnMcDeath;

        CompositionTarget.Rendering += OnFrame;

        mainTick.Tick += (s, e) => OnTick();
        fastTick.Tick += (s, e) => OnFastTick();
        topTick.Tick += (s, e) => { if (!hidden) Native.BumpTopmost(this); };
        blinkTick.Tick += (s, e) => Blink();
        bubbleHide.Tick += (s, e) =>
        {
            bubbleHide.Stop();
            BubbleRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)));
        };
        saveTick.Tick += (s, e) => Persist();

        mainTick.Start(); fastTick.Start(); topTick.Start(); saveTick.Start();
        ResetBlink(); blinkTick.Start();

        if (Application.Current != null)
            Application.Current.SessionEnding += (s, e) => Persist();
    }

    void OnLoadedOnce(object s, RoutedEventArgs e)
    {
        Native.BumpTopmost(this);
        Title = PetName;
        ApplyScale(Store.Config.petScale);
        ApplyMode(Store.Config.skinMode, announce: false);
        bool newPlan = planner.EnsureToday();
        if (firstRun) ShowBubble(Dialogue.FirstRun, 12);
        else if (newPlan) ShowBubble(Dialogue.PlanMade(planner.Describe()), 10);
        else ShowBubble(Dialogue.Welcome, 6);
        try
        {
            if (Store.Config.debugOpenShop) ShowShop();
            if (Store.Config.debugOpenStatus) ShowStatus();
            if (Store.Config.debugOpenPantry) ShowPantry(ConsumeKind.Food);
        }
        catch (Exception ex) { Store.Log("[debug] 打开窗口异常:" + ex); }
        UpdateAll();
        LogShotRect();
    }

    // ============================== 状态推进 ==============================

    void OnTick()
    {
        var now = DateTime.Now;
        double dt = (now - lastTick).TotalSeconds;
        if (dt < 0 || dt > 30) dt = 1;
        lastTick = now;

        var cst = DailyPlanner.NowCst;

        // 凌晨 0 点(东八区)重新规划
        if (planner.EnsureToday())
        {
            paidKeys.RemoveWhere(k => !k.StartsWith(planner.PlanDate));   // 清掉旧日期的工资记录
            ShowBubble(Dialogue.PlanMade(planner.Describe()), 10);
        }

        // 作息:睡觉
        bool sleepNow = !Store.Config.debugIgnoreSchedule && DailyPlanner.IsSleepTime();
        if (sleepNow != sleeping)
        {
            sleeping = sleepNow;
            if (sleeping) { walking = false; ShowBubble(Dialogue.GoodNight, 6); Store.Log("进入睡觉时间"); }
            else
            {
                state.Happy = Math.Max(state.Happy, 600);   // 睡醒心情不错
                state.AloneSeconds = 0;
                ShowBubble(Dialogue.GoodMorning, 6); Store.Log("睡醒了");
            }
        }

        // 检测使用者是否正在听歌(她自己放的不算)→ 听歌模式:缓慢加快乐、独处冻结
        UpdateListenMode(now);

        // 规划任务
        var t = (sleeping || Store.Config.debugIgnoreSchedule) ? null : planner.CurrentTask();
        curTask = t;
        string tn = t?.Name;
        if (tn != taskNow)
        {
            if (taskNow != null) { ShowBubble(Dialogue.TaskDone(taskNow), 6); Store.Log($"任务结束:{taskNow}"); }
            if (tn != null)
            {
                walking = false;
                if (mischief.Active) EndMischief(false, silent: true);
                ShowBubble(Dialogue.TaskStart(tn), 6); Store.Log($"任务开始:{tn}");
            }
            taskNow = tn;
        }

        state.Tick(dt, Paused, listenMode && !petMusicPlaying, petMusicPlaying);

        // 一次听歌自然结束(不是被陪伴打断)→ 重新武装:同一周期内还能再听
        bool listeningNow = state.IsListening;
        if (wasSelfListening && !listeningNow && state.AloneSeconds > 60)
        {
            state.RearmMusic();
            Store.Log($"这一首听完了,再独处 {(int)((state.MusicTriggerAt - state.AloneSeconds) / 60)} 分钟会再放一次");
        }
        wasSelfListening = listeningNow;

        // 饥饿/口渴过低:先等使用者投喂,没等到就自己买东西吃(金币允许)
        AutoEatIfNeeded(now);

        // 工作任务发工资(含补发:今天已结束但还没发过的)
        int nowMin = cst.Hour * 60 + cst.Minute;
        foreach (var wt in planner.Tasks)
        {
            if (!wt.IsWork || wt.EndMinutes > nowMin) continue;
            string key = $"{planner.PlanDate}|{wt.Name}|{wt.StartMinutes}";
            if (!paidKeys.Add(key)) continue;
            coins += wt.Pay;
            if (nowMin - wt.EndMinutes <= 2)
                ShowBubble(Dialogue.WorkDone(wt.Name, wt.Pay, coins), 9);
            Store.Log($"工作「{wt.Name}」完成,+{wt.Pay} 金币 → 共 {coins}");
            Persist();
        }

        // 不在“想听歌”的状态了(被陪伴/听够了)→ 自己把音乐停掉
        if (petMusicPlaying && !state.IsListening)
            StopPetMusic(Dialogue.MusicStopByBored, "听歌时间结束");

        // 独处 1~2 小时 → 自己打开听歌软件放歌;之后每听完一首,隔 25~45 分钟还会再听
        if (!Paused && !listenMode && !state.WantsMischief() && state.ShouldStartMusic())
        {
            state.MusicFiredThisCycle = true;
            var ml = MusicLauncher.Launch();
            state.ListeningUntil = now.AddSeconds(25 * 60 / Math.Max(1.0, Store.Config.timeScale));
            ShowBubble(Dialogue.MusicGo(ml?.Name), 9);
            Store.Log($"独处 {(int)(state.AloneSeconds / 60)} 分钟,自己去听歌:{ml?.Name ?? "打开失败"}");
            if (ml is { CanAutoPlay: true } && Store.Config.musicAutoPlay)
            {
                playTimer?.Stop();
                playTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, ml.PlayDelaySeconds)) };
                playTimer.Tick += (s2, e2) =>
                {
                    ((DispatcherTimer)s2).Stop();
                    if (!state.IsListening) return;   // 等加载的间隙主人回来了,就不放了
                    Native.SendMediaPlayPause();
                    petMusicPlaying = true;
                    Store.Log("已按下媒体播放键,音乐开始播放 ♪");
                };
                playTimer.Start();
            }
            else if (ml is { CanAutoPlay: false })
            {
                Store.Log("网页版播放器无法自动开始播放(浏览器限制)");
            }
        }

        // ≥3 小时没人陪且无聊值见底 → 抢鼠标求陪伴
        if (!Paused && !mischief.Active && Store.Config.mischiefEnabled
            && state.WantsMischief() && now >= mischiefCooldownUntil)
        {
            walking = false;
            StopPetMusic(null, "要去抢鼠标了,没心情听歌");
            mischief.Start();
            mischiefEpisodeEnd = now.AddSeconds(8 + rnd.Next(0, 5));
            ShowBubble(Dialogue.MischiefStart, 7);
        }
        if (mischief.Active && now >= mischiefEpisodeEnd)
            EndMischief(caught: false);

        // 自主走动:溜达时段概率大增,平时偶尔散步
        if (!Paused && !mischief.Active && !walking && !dragging)
        {
            double chance = Store.Config.debugWalkChance >= 0 ? Store.Config.debugWalkChance
                          : planner.IsWalkTime(cst) ? 0.012      // 活跃时段:平均约 1.5 分钟一次
                          : 0.0009;                              // 平时:平均约 18 分钟一次
            if (rnd.NextDouble() < chance) StartWalk();
        }

        // 主动撒娇弹话
        if (!Paused && !mischief.Active && now >= nextSassyAt)
        {
            ShowBubble(SassyLine(), 7);
            ScheduleSassy();
        }

        UpdateAll();
    }

    void OnFastTick()
    {
        // 自主走动
        if (walking && !hidden)
        {
            const double dt = 0.03;
            double dx = walkTarget.X - Left, dy = walkTarget.Y - Top;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double step = walkSpeed * dt;
            if (dist <= step)
            {
                Left = walkTarget.X; Top = walkTarget.Y;
                walking = false;
                ClampToScreen();
                Persist();
                LogShotRect();
                if (rnd.NextDouble() < 0.3) ShowBubble(Dialogue.WalkDone, 4);
            }
            else
            {
                Left += dx / dist * step;
                Top += dy / dist * step;
            }
        }

        // 抢鼠标
        if (!mischief.Active || hidden) return;
        try
        {
            var center = PetRoot.PointToScreen(new Point(110, 110));
            if (mischief.Update(center))
                EndMischief(caught: true);
        }
        catch { EndMischief(false, silent: true); }
    }

    void StartWalk()
    {
        var wa = SystemParameters.WorkArea;
        // 目标点:当前位置向随机方向走 160~650(限制在工作区内,窗口完整可见)
        double ang = rnd.NextDouble() * Math.PI * 2;
        double len = 160 + rnd.NextDouble() * 490;
        double tx = Left + Math.Cos(ang) * len;
        double ty = Top + Math.Sin(ang) * len * 0.6;   // 纵向少走点,更像在“地面”移动
        tx = Math.Min(Math.Max(tx, wa.Left + 4), wa.Right - Width - 4);
        ty = Math.Min(Math.Max(ty, wa.Top + 4), wa.Bottom - Height - 4);
        walkTarget = new Point(tx, ty);
        walkSpeed = 110 + rnd.NextDouble() * 70;   // DIP/秒
        walking = true;
        if (rnd.NextDouble() < 0.35) ShowBubble(Dialogue.WalkStart, 4);
        Store.Log($"出门溜达 → ({(int)tx},{(int)ty})");
    }

    /// <summary>她自己把音乐停掉(只有确实是她放的才停)。</summary>
    void StopPetMusic(string line, string reason)
    {
        playTimer?.Stop();
        state.ListeningUntil = DateTime.MinValue;
        if (!petMusicPlaying) return;
        petMusicPlaying = false;
        Native.SendMediaPlayPause();
        if (line != null) ShowBubble(line, 5);
        Store.Log($"自己停止了音乐({reason})");
    }

    void EndMischief(bool caught, bool silent = false)
    {
        mischief.Stop();
        if (caught)
        {
            int joy = RollJoy();
            state.Interact(joy);
            happyUntil = DateTime.Now.AddSeconds(5);
            if (!silent) ShowBubble(Dialogue.MischiefCaught, 6);
            mischiefCooldownUntil = DateTime.Now.AddMinutes(2);
            Store.Log($"使用者靠近安抚,恶作剧结束,快乐值+{joy}");
        }
        else
        {
            if (!silent) ShowBubble(Dialogue.MischiefGiveup, 5);
            mischiefCooldownUntil = DateTime.Now.AddMinutes(3 + rnd.NextDouble() * 3);
            Store.Log("这一轮没等到陪伴,稍后再闹");
        }
        UpdateAll();
    }

    string SassyLine()
    {
        if (state.Hunger < 25) return Dialogue.Hungry;
        if (state.Thirst < 25) return Dialogue.Thirsty;
        if (listenMode || state.IsListening) return Dialogue.SassyListening;
        double b = state.HappyPercent * 100;
        if (b > 70) return Dialogue.SassyContent;
        if (b > 40) return Dialogue.SassySlight;
        if (b > 15) return Dialogue.SassyBored;
        return Dialogue.SassyCritical;
    }

    void ScheduleSassy()
    {
        double b = state.HappyPercent * 100;
        double minutes = b > 70 ? 12 + rnd.NextDouble() * 10
                       : b > 40 ? 6 + rnd.NextDouble() * 7
                       : 2.5 + rnd.NextDouble() * 5;
        nextSassyAt = DateTime.Now.AddMinutes(minutes);
    }

    // ============================== 互动 ==============================

    void Interact(double amt, string line, string kind)
    {
        if (taskNow != null) { ShowBubble(Dialogue.TaskRefuse(taskNow), 4); return; }
        if (sleeping)
        {
            state.Interact(Math.Min(5, amt));
            SayForInteraction(kind, Dialogue.SleepPoke, sleeping: true);
            UpdateAll();
            return;
        }
        state.Interact(amt);
        walking = false;
        happyUntil = DateTime.Now.AddSeconds(4.5);
        if (mischief.Active) EndMischief(caught: true, silent: true);
        bool wasPlaying = petMusicPlaying;
        StopPetMusic(null, "主人来陪了");
        SayForInteraction(kind, wasPlaying ? Dialogue.MusicStopByCompany : line);
        WaveArm();
        ScheduleSassy();
        Store.Log($"互动[{kind}] +{amt} → 快乐值 {(int)state.Happy}");
        UpdateAll();
        Persist();
    }

    /// <summary>
    /// 说出互动台词:本地模式直接用传入的 JSON 台词;大模型模式改为异步向 API 要一句,
    /// 6 秒内没返回(或失败)就静默跳过本次台词——快乐值/动画等其它效果已先行生效,不受影响。
    /// </summary>
    void SayForInteraction(string kind, string localLine, bool sleeping = false)
    {
        if (!LlmClient.Enabled) { ShowBubble(localLine, sleeping ? 4 : 5); return; }
        _ = SayViaLlm(kind, sleeping);
    }

    async Task SayViaLlm(string kind, bool sleeping)
    {
        string scene = sleeping
            ? $"主人趁{PetName}睡觉时轻轻戳了戳她"
            : kind switch
            {
                "摸摸头" or "点击摸摸" => $"主人摸了摸{PetName}的头",
                "逗她玩" => $"主人逗{PetName}玩",
                "聊天" => $"主人过来和{PetName}聊天",
                "拖拽散步" => $"主人拎着{PetName}散步",
                "挪动" => $"主人把{PetName}挪了个位置",
                _ => $"主人和{PetName}互动了一下({kind})"
            };
        string situation = $"快乐值{(int)state.Happy}/1000,饥饿{(int)state.Hunger}/100,口渴{(int)state.Thirst}/100"
                         + (sleeping ? ",正在睡觉被吵醒" : "");
        string reply = await LlmClient.ChatAsync(scene, situation);
        if (reply == null) return;               // 超时/失败:本次不出气泡,其它功能照常
        if (hidden) return;
        ShowBubble(reply, sleeping ? 4 : 5);
    }

    void OnLBDown(object s, MouseButtonEventArgs e)
    {
        walking = false;   // 抓住她就不走了
        if (mischief.Active) { EndMischief(caught: true); return; }
        if (taskNow != null) { ShowBubble(Dialogue.TaskRefuse(taskNow), 4); return; }
        dragging = true; dragTotal = 0;
        dragStartCursor = CursorDip();
        dragStartWin = new Point(Left, Top);
        CaptureMouse();
    }

    void OnMMove(object s, MouseEventArgs e)
    {
        if (!dragging) return;
        var c = CursorDip();
        Left = dragStartWin.X + (c.X - dragStartCursor.X);
        Top = dragStartWin.Y + (c.Y - dragStartCursor.Y);
        double dx = c.X - dragStartCursor.X, dy = c.Y - dragStartCursor.Y;
        dragTotal = Math.Max(dragTotal, Math.Sqrt(dx * dx + dy * dy));
    }

    void OnLBUp(object s, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        ClampToScreen();
        LogShotRect();
        if (dragTotal >= 90) Interact(RollJoy(), Dialogue.DragWalk, "拖拽散步");
        else if (dragTotal <= 6) Interact(RollJoy(), Dialogue.Pat, "点击摸摸");
        else Interact(RollJoy(), Dialogue.DragShort, "挪动");
    }

    /// <summary>普通互动(投喂/送礼除外)的快乐值收益:随机 25~50。</summary>
    int RollJoy() => rnd.Next(25, 51);

    Point CursorDip()
    {
        Native.GetCursorPos(out var p);
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget == null) return new Point(p.X, p.Y);
        return src.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
    }

    // ============================== 右键菜单 ==============================

    void SetupMenu()
    {
        petMenu = new ContextMenu();
        miHead = new MenuItem { IsEnabled = false, FontWeight = FontWeights.Bold };
        miPat = Item("摸摸头", () => Interact(RollJoy(), Dialogue.Pat, "摸摸头"));
        miPlay = Item("逗她玩", () => { Bounce(); Interact(RollJoy(), Dialogue.Play, "逗她玩"); });
        miChat = Item("陪她聊天", () => Interact(RollJoy(), Dialogue.Chat, "聊天"));
        miShop = Item("礼物商店…", ShowShop);
        miStatus = Item("查看状态…", ShowStatus);
        miFood = Item("投喂食物…", () => ShowPantry(ConsumeKind.Food));
        miDrink = Item("投喂饮料…", () => ShowPantry(ConsumeKind.Drink));
        miPlan = Item("今日计划…", ShowPlan);
        miReplan = Item("重新规划今天", () =>
        {
            planner.Replan();
            ShowBubble(Dialogue.PlanMade(planner.Describe()), 10);
            Store.Log("使用者要求重新规划今天");
            UpdateAll();
        });

        // 外观三模式
        miLookOfficial = Item("官方形象(内置矢量)", () => ApplyMode("official", true));
        miLookCustom = Item("自定义皮肤(skin/PNG)", () => ApplyMode("custom", true));
        miLookLive2d = Item("Live2D 模型(live2d/)", () => ApplyMode("live2d", true));
        miLookGif = Item("GIF 动画模式(gif/)", () => ApplyMode("gif", true));
        miLook = new MenuItem { Header = "外观切换" };
        miLook.Items.Add(miLookOfficial);
        miLook.Items.Add(miLookCustom);
        miLook.Items.Add(miLookGif);
        miLook.Items.Add(miLookLive2d);
        miLook.Items.Add(new Separator());
        miLook.Items.Add(Item("打开皮肤目录(exe 旁)", () =>
            Process.Start(new ProcessStartInfo(AppContext.BaseDirectory) { UseShellExecute = true })));

        // 对话模式:本地 JSON 随机 / 大模型 API
        miTalkLocal = Item("本地台词(JSON 随机)", () => ApplyDialogueMode("local", true));
        miTalkLlm = Item("大模型对话(需配置)", () => ApplyDialogueMode("llm", true));
        miLlmSetup = Item("大模型设置 / 人设…", ShowLlmSetup);
        miTalk = new MenuItem { Header = "对话模式" };
        miTalk.Items.Add(miTalkLocal);
        miTalk.Items.Add(miTalkLlm);
        miTalk.Items.Add(new Separator());
        miTalk.Items.Add(miLlmSetup);

        miMc = new MenuItem { Header = "我的世界" };
        miMcLocal = Item("连接本机(127.0.0.1)", () => StartMc("127.0.0.1", 25565));
        miMcLan = Item("连接局域网…", ShowLanConnect);
        miMcInstall = Item("安装MC模块(npm install…)", InstallMcModule);
        miMcFrp = Item("局域网/内网穿透…", () => ShowFrpConnect());
        miMcCloud = Item("连接云服务器…", () => ShowCloudConnect());
        miMcChat = Item("MC中说句话…", ShowMcChatBox);
        miMc.Items.Add(miMcLocal);
        miMc.Items.Add(miMcLan);
        miMc.Items.Add(miMcFrp);
        miMc.Items.Add(miMcCloud);
        miMc.Items.Add(new Separator());
        miMcNode = Item("设置node.exe路径…", ShowNodePathSetup);
        miMc.Items.Add(miMcChat);
        miMc.Items.Add(miMcInstall);
        miMc.Items.Add(miMcNode);
        miMcChat = Item("MC中说句话…", ShowMcChatBox);
        miRename = Item("给她改名…", ShowRename);
        miHide = Item("躲进托盘休息", HideToTray);
        miDir = Item("打开数据文件夹", () =>
            Process.Start(new ProcessStartInfo(Store.Dir) { UseShellExecute = true }));
        miExit = Item("退出", ExitApp);

        petMenu.Items.Add(miHead);
        petMenu.Items.Add(new Separator());
        petMenu.Items.Add(miPat);
        petMenu.Items.Add(miPlay);
        petMenu.Items.Add(miChat);
        petMenu.Items.Add(new Separator());
        petMenu.Items.Add(miStatus);
        petMenu.Items.Add(miFood);
        petMenu.Items.Add(miDrink);
        petMenu.Items.Add(miShop);
        petMenu.Items.Add(new Separator());
        petMenu.Items.Add(miPlan);
        petMenu.Items.Add(miReplan);
        petMenu.Items.Add(miLook);
        
        var miScale = new MenuItem { Header = "调整大小" };
        foreach (var sc in new[] { 0.8, 1.0, 1.2, 1.5, 2.0 })
        {
            double val = sc;
            var item = Item($"{val:F1} 倍", () => ApplyScale(val));
            miScale.Items.Add(item);
        }
        petMenu.Items.Add(miScale);

        petMenu.Items.Add(miTalk);
        petMenu.Items.Add(miMc);
        petMenu.Items.Add(miMcChat);
        petMenu.Items.Add(miRename);
        petMenu.Items.Add(miMcName);
        petMenu.Items.Add(miHide);
        petMenu.Items.Add(miDir);
        petMenu.Items.Add(new Separator());
        petMenu.Items.Add(miExit);

        // NOACTIVATE 窗口上,ContextMenuService 弹的菜单会立即关闭:改为手动弹出
        MouseRightButtonUp += (s, e) => { OpenPetMenu(); e.Handled = true; };
        petMenu.Closed += (s, e) => Native.SetClickThrough(this, false);   // 恢复永不抢焦点
    }

    void OpenPetMenu()
    {
        miPat.IsEnabled = miPlay.IsEnabled = miChat.IsEnabled = taskNow == null;
        miFood.IsEnabled = miDrink.IsEnabled = taskNow == null && !sleeping;
        miLookOfficial.IsChecked = skinMode == "official";
        miLookCustom.IsChecked = skinMode == "custom";
        miLookGif.IsChecked = skinMode == "gif";
        miLookLive2d.IsChecked = skinMode == "live2d";
        miTalkLocal.IsChecked = Store.Config.dialogueMode != "llm";
        miTalkLlm.IsChecked = Store.Config.dialogueMode == "llm";
        miMcChat.IsEnabled = mc.Running;
        miMcLocal.IsEnabled = true;
        miMcLan.IsEnabled = true;
        miMcFrp.IsEnabled = true;
        miMcCloud.IsEnabled = true;
        miHead.Header = (taskNow != null ? $"{PetName}(任务中:{taskNow})"
                      : sleeping ? $"{PetName}(睡觉中…小声点)"
                      : $"{PetName} · 快乐 {(int)state.Happy}/1000") + $" · 金币{coins}";

        // 临时允许激活,菜单才能正常打开与交互
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero)
        {
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_NOACTIVATE);
        }
        try { Activate(); } catch { }
        petMenu.PlacementTarget = this;
        petMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        petMenu.IsOpen = true;
    }

    static MenuItem Item(string header, Action act)
    {
        var m = new MenuItem { Header = header };
        m.Click += (s, e) => act();
        return m;
    }

    // ============================== 改名 ==============================

    Window renameWin;

    void ShowRename()
    {
        if (renameWin != null) { renameWin.Activate(); return; }
        var sp = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        sp.Children.Add(new TextBlock
        {
            Text = "给她起个新名字(最长 12 字):",
            Foreground = Brushes.White, FontSize = 13,
            FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 0, 0, 8)
        });
        var tb = new TextBox { Text = PetName, FontSize = 14, MaxLength = 12, Padding = new Thickness(6, 4, 6, 4) };
        sp.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "就叫这个!", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), FontFamily = new FontFamily("Microsoft YaHei UI") };
        var cancel = new Button { Content = "算了", Padding = new Thickness(12, 4, 12, 4), FontFamily = new FontFamily("Microsoft YaHei UI") };
        row.Children.Add(ok); row.Children.Add(cancel);
        sp.Children.Add(row);

        renameWin = new Window
        {
            Title = "改名",
            Content = sp, Width = 300, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
            Topmost = true, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x24, 0x30)),
        };
        renameWin.Left = Math.Max(SystemParameters.VirtualScreenLeft, Left - 320);
        renameWin.Top = Math.Max(SystemParameters.VirtualScreenTop, Top + 80);
        renameWin.Closed += (s, e) => renameWin = null;

        ok.Click += (s, e) =>
        {
            string n = tb.Text.Trim();
            if (n.Length == 0) { tb.Text = PetName; return; }
            string old = PetName;
            Store.Config.petName = n;
            Store.SaveConfig();
            Title = n;
            if (tray != null) tray.Text = n;
            UpdateBar();
            ShowBubble(Dialogue.Rename(n, old), 8);
            Store.Log($"桌宠改名:「{old}」→「{n}」");
            renameWin.Close();
        };
        cancel.Click += (s, e) => renameWin.Close();
        renameWin.Show();
        tb.Focus(); tb.SelectAll();
    }

    // ============================== 对话模式 / 大模型设置 ==============================

    void ApplyDialogueMode(string mode, bool announce)
    {
        mode = mode == "llm" ? "llm" : "local";
        Store.Config.dialogueMode = mode;
        Store.SaveConfig();
        Store.Log($"对话模式切换为 {mode}");
        if (!announce) return;

        if (mode == "llm")
        {
            if (LlmClient.Enabled)
                ShowBubble("接上大模型啦~现在吾会自己组织语言陪主人聊天!", 6);
            else
            {
                ShowBubble("想用大模型对话,得先填接口地址、密钥和模型哦~\n(右键「对话模式 → 大模型设置」)", 9);
                ShowLlmSetup();
            }
        }
        else ShowBubble("切回本地台词啦,还是熟悉的吾~", 5);
    }

    Window llmWin;

    void ShowLlmSetup()
    {
        if (llmWin != null) { llmWin.Activate(); return; }
        var c = Store.Config;
        var sp = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        var white = Brushes.White;
        var yahei = new FontFamily("Microsoft YaHei UI");

        sp.Children.Add(new TextBlock
        {
            Text = "🤖 大模型对话设置(OpenAI 兼容接口)",
            FontSize = 14, FontWeight = FontWeights.Bold, Foreground = white,
            FontFamily = yahei, Margin = new Thickness(0, 0, 0, 10)
        });

        TextBox Field(string label, string value, string hint = null, bool pwd = false)
        {
            sp.Children.Add(new TextBlock
            {
                Text = label, Foreground = white, FontSize = 12.5,
                FontFamily = yahei, Margin = new Thickness(0, 6, 0, 2)
            });
            var t = new TextBox
            {
                Text = value ?? "", FontSize = 13, Padding = new Thickness(6, 4, 6, 4),
                FontFamily = yahei
            };
            sp.Children.Add(t);
            if (hint != null)
                sp.Children.Add(new TextBlock
                {
                    Text = hint, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
                    FontSize = 10.5, FontFamily = yahei, Margin = new Thickness(0, 1, 0, 0), TextWrapping = TextWrapping.Wrap
                });
            return t;
        }

        var url = Field("接口地址 baseUrl", c.llmBaseUrl, "例:https://api.openai.com/v1(会自动拼 /chat/completions)");
        var key = Field("API 密钥", c.llmApiKey, "sk-… 只保存在本机 config.json,不上传别处");
        var model = Field("模型名", c.llmModel, "例:gpt-4o-mini / deepseek-chat 等");

        sp.Children.Add(new TextBlock
        {
            Text = "人设(可自由修改,用 {name} 代表桌宠名字):",
            Foreground = white, FontSize = 12.5, FontFamily = yahei, Margin = new Thickness(0, 8, 0, 2)
        });
        var persona = new TextBox
        {
            Text = c.llmPersona, FontSize = 12.5, FontFamily = yahei,
            Padding = new Thickness(6, 4, 6, 4),
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            Height = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        sp.Children.Add(persona);

        var tip = new TextBlock
        {
            Text = "约束已内置:只输出中文、单句、不超时(默认 6 秒无响应即跳过本次台词,不影响其它功能)。",
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            FontSize = 10.5, FontFamily = yahei, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        };
        sp.Children.Add(tip);

        var status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x7A)),
            FontSize = 12, FontFamily = yahei, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        };
        sp.Children.Add(status);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var test = new Button { Content = "测试连接", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), FontFamily = yahei };
        var save = new Button { Content = "保存并启用", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), FontFamily = yahei };
        var cancel = new Button { Content = "关闭", Padding = new Thickness(12, 4, 12, 4), FontFamily = yahei };
        row.Children.Add(test); row.Children.Add(save); row.Children.Add(cancel);
        sp.Children.Add(row);

        void Apply()
        {
            c.llmBaseUrl = url.Text.Trim();
            c.llmApiKey = key.Text.Trim();
            c.llmModel = model.Text.Trim();
            c.llmPersona = persona.Text;
            Store.SaveConfig();
        }

        test.Click += async (s, e) =>
        {
            Apply();
            string prevMode = c.dialogueMode;
            c.dialogueMode = "llm";            // 让 TestAsync 的 Enabled 判定通过
            status.Text = "正在测试…";
            test.IsEnabled = save.IsEnabled = false;
            var (ok, text) = await LlmClient.TestAsync();
            c.dialogueMode = prevMode;
            test.IsEnabled = save.IsEnabled = true;
            status.Text = ok ? $"✅ 连通!她说:{text}" : $"❌ {text}";
        };
        save.Click += (s, e) =>
        {
            Apply();
            ApplyDialogueMode("llm", false);
            status.Text = LlmClient.Enabled ? "✅ 已保存并切到大模型对话模式。" : "已保存,但配置仍不完整(需地址/密钥/模型齐全)。";
            if (LlmClient.Enabled) ShowBubble("设置好啦~接下来由吾自己想话说!", 6);
        };
        cancel.Click += (s, e) => llmWin.Close();

        llmWin = new Window
        {
            Title = "大模型对话设置",
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Width = 420, Height = 560,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
            Topmost = true, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x24, 0x30)),
        };
        llmWin.Left = Math.Max(SystemParameters.VirtualScreenLeft, Left - 440);
        llmWin.Top = Math.Max(SystemParameters.VirtualScreenTop, Top - 60);
        llmWin.Closed += (s, e) => llmWin = null;
        llmWin.Show();
    }

    void ShowPlan()
    {
        if (planWin != null) { planWin.Activate(); return; }
        var cst = DailyPlanner.NowCst;
        var sp = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        sp.Children.Add(new TextBlock
        {
            Text = $"📋 {PetName}的今日计划({cst:MM月dd日})",
            FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 0, 0, 10)
        });
        int nowMin = cst.Hour * 60 + cst.Minute;
        if (planner.Tasks.Count == 0)
            sp.Children.Add(new TextBlock { Text = "今天没有安排任务,全天陪主人!", Foreground = Brushes.White, FontSize = 13 });
        foreach (var i in planner.Tasks)
        {
            string mark = nowMin >= i.EndMinutes ? "✅" : nowMin >= i.StartMinutes ? "▶" : "🕒";
            string pay = i.IsWork ? $"(工作 +{i.Pay}金币)" : "";
            sp.Children.Add(new TextBlock
            {
                Text = $"{mark}  {DailyPlanner.Fmt(i.StartMinutes)} - {DailyPlanner.Fmt(i.EndMinutes)}   {i.Name}{pay}",
                FontSize = 13, Foreground = i.IsWork ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x7A)) : Brushes.White,
                Margin = new Thickness(0, 3, 0, 3),
                FontFamily = new FontFamily("Microsoft YaHei UI")
            });
        }
        sp.Children.Add(new TextBlock
        {
            Text = $"\n爱溜达时段:{planner.WalksDescribe()}\n固定作息:23:30 ~ 07:00 睡觉\n任务中快乐值暂停计时,也不能拖拽吾哦!\n完成「工作」任务赚金币(75~200)→ 右键商店买礼物/食物\n任务库:%APPDATA%\\心海海\\tasks.txt(可编辑,\n改完右键「重新规划今天」立即生效)",
            FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC7, 0xD0)),
            FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 8, 0, 0)
        });
        planWin = new Window
        {
            Title = $"{PetName}的今日计划",
            Content = sp,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Topmost = true, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x27, 0x33)),
        };
        planWin.Left = Math.Max(SystemParameters.VirtualScreenLeft, Left - 250);
        planWin.Top = Math.Max(SystemParameters.VirtualScreenTop, Top + 60);
        planWin.Closed += (s, e) => planWin = null;
        planWin.Show();
    }

    // ============================== 礼物商店 ==============================

    TextBlock shopCoinText;
    StackPanel shopList;

    void ShowShop()
    {
        if (shopWin != null) { RefreshShop(); shopWin.Activate(); return; }

        var root = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        root.Children.Add(new TextBlock
        {
            Text = $"🎁 {PetName}的礼物商店",
            FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 0, 0, 4)
        });
        shopCoinText = new TextBlock
        {
            FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x7A)),
            FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(shopCoinText);

        shopList = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            Content = shopList, MaxHeight = 430,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        root.Children.Add(new TextBlock
        {
            Text = "\n完成每日「工作」任务赚金币(每次 75~200);礼物越稀有越贵。\n带“加护”的礼物可让她一段时间保持满快乐值。\n(任务中/睡觉时她收不了礼物)",
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        });

        shopWin = new Window
        {
            Title = $"{PetName}的礼物商店",
            Content = root,
            Width = 400, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Topmost = true, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x24, 0x2E)),
        };
        shopWin.Left = Math.Max(SystemParameters.VirtualScreenLeft, Left - 420);
        shopWin.Top = Math.Max(SystemParameters.VirtualScreenTop, Top - 60);
        shopWin.Closed += (s, e) => { shopWin = null; shopList = null; shopCoinText = null; };
        RefreshShop();
        shopWin.Show();
    }

    static string FmtLock(double hours) =>
        hours < 1 ? $"{(int)Math.Round(hours * 60)}分钟"
        : hours < 24 ? $"{hours:0.#}小时"
        : $"{hours / 24:0.#}天";

    void RefreshShop()
    {
        if (shopList == null) return;
        shopCoinText.Text = $"金币:{coins} 枚";
        shopList.Children.Clear();
        foreach (var g in Shop.Gifts)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 6, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });

            var icon = new TextBlock { Text = g.Icon, FontSize = 18, FontFamily = new FontFamily("Segoe UI Emoji"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(icon, 0);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameLine = new TextBlock { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 12.5 };
            nameLine.Inlines.Add(new System.Windows.Documents.Run(g.Name) { Foreground = Brushes.White, FontWeight = FontWeights.Bold });
            nameLine.Inlines.Add(new System.Windows.Documents.Run($"  {g.RarityText}")
            { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(g.RarityColor)), FontSize = 11 });
            mid.Children.Add(nameLine);
            mid.Children.Add(new TextBlock
            {
                Text = g.LockHours > 0 ? $"加护:满快乐值保持 {FmtLock(g.LockHours)}" : $"快乐值 +{(int)g.HappyAdd}",
                FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
                FontFamily = new FontFamily("Microsoft YaHei UI")
            });
            Grid.SetColumn(mid, 1);

            var price = new TextBlock
            {
                Text = $"{g.Price} 金币", VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x7A)),
                FontSize = 12, FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(price, 2);

            var buy = new Button
            {
                Content = "赠送", Width = 56, Height = 26,
                IsEnabled = coins >= g.Price && taskNow == null && !sleeping,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
            };
            buy.Click += (s, e) => BuyGift(g);
            Grid.SetColumn(buy, 3);

            row.Children.Add(icon); row.Children.Add(mid); row.Children.Add(price); row.Children.Add(buy);
            shopList.Children.Add(row);
        }
    }

    void BuyGift(Gift g)
    {
        if (taskNow != null) { ShowBubble(Dialogue.GiftBusy, 5); return; }
        if (sleeping) { ShowBubble(Dialogue.GiftSleep, 5); return; }
        if (coins < g.Price) { ShowBubble(Dialogue.NoCoins, 5); return; }

        coins -= g.Price;
        state.Interact(g.HappyAdd);
        if (g.LockHours > 0)
        {
            state.LockFullUntil = DateTime.Now.AddHours(g.LockHours);
            Store.Log($"礼物加护生效:满快乐值保持 {FmtLock(g.LockHours)}(到 {state.LockFullUntil:MM-dd HH:mm})");
        }
        walking = false;
        happyUntil = DateTime.Now.AddSeconds(6);
        if (mischief.Active) EndMischief(true, silent: true);
        StopPetMusic(null, "收到礼物");
        Bounce(); WaveArm();
        ShowBubble(Dialogue.GiftLine(g), 9);
        ScheduleSassy();
        Store.Log($"收到礼物「{g.Name}」({g.RarityText},{g.Price}金币),余额 {coins}");
        Persist();
        UpdateAll();
        RefreshShop();
    }

    // ============================== 听歌模式 ==============================

    void UpdateListenMode(DateTime now)
    {
        if (!Store.Config.listenModeEnabled || petMusicPlaying)
        {
            if (listenMode) { listenMode = false; }
            return;
        }
        if (now < nextAudioCheck) return;
        nextAudioCheck = now.AddSeconds(2);

        bool playerRunning = MusicLauncher.DetectRunningPlayer() != null || Store.Config.debugAssumePlayer;
        float peak = playerRunning ? AudioMeter.Peak() : -1;
        bool soundNow = playerRunning && peak >= 0.008f;   // 有播放器且确有声音

        if (soundNow) { audioHitStreak++; audioMissStreak = 0; }
        else { audioMissStreak++; audioHitStreak = 0; }

        if (!listenMode && audioHitStreak >= 2 && !Paused)   // 连续约 4 秒有声 → 进入
        {
            listenMode = true;
            ShowBubble(Dialogue.ListenModeOn, 6);
            Store.Log($"检测到使用者在听歌(峰值{peak:0.00}),切入听歌模式,快乐值缓慢上升");
        }
        else if (listenMode && audioMissStreak >= 4)         // 连续约 8 秒没声 → 退出
        {
            listenMode = false;
            Store.Log("使用者的音乐停了,退出听歌模式");
        }
    }

    // ============================== 自主进食 ==============================

    void AutoEatIfNeeded(DateTime now)
    {
        if (Paused || mischief.Active) return;
        double th = Store.Config.autoEatThreshold;
        bool need = state.Hunger < th || state.Thirst < th;
        if (!need) { nextAutoEatAt = DateTime.MinValue; return; }

        // 第一次察觉到饿/渴:给使用者一个投喂的宽限期(真实时间约 3 分钟,受 timeScale 压缩)
        if (nextAutoEatAt == DateTime.MinValue)
        {
            double graceSec = 180 / Math.Max(1.0, Store.Config.timeScale);
            nextAutoEatAt = now.AddSeconds(graceSec);
            ShowBubble(state.Hunger <= state.Thirst ? Dialogue.Hungry : Dialogue.Thirsty, 6);
            return;
        }
        if (now < nextAutoEatAt) return;

        // 宽限期到,使用者没投喂 → 自己在金币允许范围内挑一样吃/喝
        bool wantFood = state.Hunger <= state.Thirst;
        var pool = (wantFood ? Pantry.Foods : Pantry.Drinks).Where(c => c.Price <= coins).ToList();
        if (pool.Count == 0) pool = Pantry.All.Where(c => c.Price <= coins).ToList();
        if (pool.Count == 0)   // 一分钱都没有:干着急,过会儿再看
        {
            ShowBubble(Dialogue.NoCoinsFood, 6);
            nextAutoEatAt = now.AddSeconds(120 / Math.Max(1.0, Store.Config.timeScale));
            return;
        }

        var pick = pool[rnd.Next(pool.Count)];
        Consume(pick, selfBought: true);
        nextAutoEatAt = DateTime.MinValue;
    }

    void Consume(Consumable c, bool selfBought)
    {
        if (c.Price > coins) { ShowBubble(Dialogue.NoCoinsFood, 5); return; }
        coins -= c.Price;
        state.Feed(c.Hunger, c.Thirst, c.Happy);
        walking = false;
        happyUntil = DateTime.Now.AddSeconds(4);
        Bounce();
        string prefix = selfBought ? Dialogue.SelfBuyPrefix(c) : "";
        ShowBubble(prefix + Dialogue.FoodLine(c), 7);
        Store.Log($"{(selfBought ? "自主进食" : "使用者投喂")}「{c.Name}」-{c.Price}金币,饥饿+{c.Hunger} 口渴+{c.Thirst} → 饥饿{(int)state.Hunger} 口渴{(int)state.Thirst} 金币{coins}");
        Persist();
        UpdateAll();
        RefreshPantry();
        RefreshStatus();
    }

    // ============================== 状态面板 ==============================

    StackPanel statusBody;

    void ShowStatus()
    {
        if (statusWin != null) { RefreshStatus(); statusWin.Activate(); return; }
        statusBody = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        statusWin = new Window
        {
            Title = $"{PetName}的状态",
            Content = statusBody,
            Width = 320, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Topmost = true, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x24, 0x30)),
        };
        statusWin.Left = Math.Max(SystemParameters.VirtualScreenLeft, Left - 340);
        statusWin.Top = Math.Max(SystemParameters.VirtualScreenTop, Top + 30);
        statusWin.Closed += (s, e) => { statusWin = null; statusBody = null; };
        RefreshStatus();
        statusWin.Show();
    }

    void RefreshStatus()
    {
        if (statusBody == null) return;
        statusBody.Children.Clear();
        statusBody.Children.Add(new TextBlock
        {
            Text = $"🌊 {PetName}的状态", FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, FontFamily = new FontFamily("Microsoft YaHei UI"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        AddStatBar("快乐值", state.Happy, PetState.MaxHappy, Color.FromRgb(0x7C, 0xCB, 0x7F));
        AddStatBar("饥饿值", state.Hunger, 100, Color.FromRgb(0xFF, 0xB0, 0x5A));
        AddStatBar("口渴值", state.Thirst, 100, Color.FromRgb(0x5A, 0xB8, 0xE8));

        string mood = sleeping ? "睡觉中 zzZ"
                    : taskNow != null ? $"工作中:{taskNow}"
                    : mischief.Active ? "闹脾气·抢鼠标中"
                    : state.IsLocked ? "礼物加护中,超开心"
                    : listenMode ? "和你一起听歌♪"
                    : (state.Hunger < 20 ? "饿扁了…" : state.Thirst < 20 ? "渴坏了…"
                    : state.HappyPercent > 0.7 ? "心满意足~" : state.HappyPercent > 0.4 ? "有点无聊" : "非常想你");

        var info = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Line(string s) => info.Children.Add(new TextBlock
        {
            Text = s, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xE4, 0xD6, 0xDE)),
            FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 2, 0, 2)
        });
        Line($"💰 金币:{coins}");
        Line($"😊 心情:{mood}");
        if (state.IsLocked) Line($"🎁 加护剩余:{FmtDur((int)Math.Ceiling((state.LockFullUntil - DateTime.Now).TotalMinutes))}");
        var next = planner.Tasks.FirstOrDefault(t => (DailyPlanner.NowCst.Hour * 60 + DailyPlanner.NowCst.Minute) < t.StartMinutes);
        if (next != null) Line($"🕒 下一项:{DailyPlanner.Fmt(next.StartMinutes)} {next.Name}" + (next.IsWork ? $"(+{next.Pay}金币)" : ""));
        statusBody.Children.Add(info);

        var hint = (state.Hunger < 30 || state.Thirst < 30)
            ? "\n她有点饿/渴了,右键「投喂食物/饮料」照顾一下吧。\n(你不喂,她过一会儿会自己买着吃)"
            : "\n右键菜单可以投喂、送礼、逗她玩哦~";
        statusBody.Children.Add(new TextBlock
        {
            Text = hint, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            FontFamily = new FontFamily("Microsoft YaHei UI"), TextWrapping = TextWrapping.Wrap
        });
    }

    void AddStatBar(string label, double val, double max, Color color)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });

        var lb = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Microsoft YaHei UI") };
        Grid.SetColumn(lb, 0);

        var track = new Border { Height = 14, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromRgb(0x45, 0x3A, 0x48)), VerticalAlignment = VerticalAlignment.Center };
        var fillGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        double frac = Math.Clamp(val / max, 0, 1);
        track.Loaded += (s, e) => fillGrid.Width = Math.Max(4, (track.ActualWidth) * frac);
        var fill = new Border { Height = 14, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(color) };
        fillGrid.Children.Add(fill);
        track.Child = fillGrid;
        // 立即量一次(Loaded 可能已过)
        track.SizeChanged += (s, e) => fillGrid.Width = Math.Max(4, track.ActualWidth * frac);
        Grid.SetColumn(track, 1);

        var num = new TextBlock { Text = $"{(int)Math.Round(val)}/{(int)max}", Foreground = Brushes.White, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Microsoft YaHei UI") };
        Grid.SetColumn(num, 2);

        g.Children.Add(lb); g.Children.Add(track); g.Children.Add(num);
        statusBody.Children.Add(g);
    }

    // ============================== 食堂(投喂) ==============================

    StackPanel pantryList; TextBlock pantryCoin, pantryTitle; ConsumeKind pantryKind;

    void ShowPantry(ConsumeKind kind)
    {
        pantryKind = kind;
        if (pantryWin != null) { RefreshPantry(); pantryWin.Activate(); return; }

        var root = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        pantryTitle = new TextBlock
        {
            Text = $"🍱 {PetName}的食堂",
            FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(pantryTitle);
        pantryCoin = new TextBlock
        {
            FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x7A)),
            FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 0, 0, 6)
        };
        root.Children.Add(pantryCoin);

        // 食物 / 饮料 切换
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var bFood = new Button { Content = "🍙 食物", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 3, 10, 3), FontFamily = new FontFamily("Microsoft YaHei UI") };
        var bDrink = new Button { Content = "🥤 饮料", Padding = new Thickness(10, 3, 10, 3), FontFamily = new FontFamily("Microsoft YaHei UI") };
        bFood.Click += (s, e) => { pantryKind = ConsumeKind.Food; RefreshPantry(); };
        bDrink.Click += (s, e) => { pantryKind = ConsumeKind.Drink; RefreshPantry(); };
        tabs.Children.Add(bFood); tabs.Children.Add(bDrink);
        root.Children.Add(tabs);

        pantryList = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = pantryList, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(new TextBlock
        {
            Text = "\n投喂可恢复饥饿/口渴,附带一点快乐值。\n右键「查看状态」可看当前饥饿与口渴。",
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xB4, 0xBE)),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        });

        pantryWin = new Window
        {
            Content = root, Width = 400, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
            Topmost = true, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x2C, 0x24)),
        };
        pantryWin.Title = $"{PetName}的食堂";
        pantryWin.Left = Math.Max(SystemParameters.VirtualScreenLeft, Left - 420);
        pantryWin.Top = Math.Max(SystemParameters.VirtualScreenTop, Top);
        pantryWin.Closed += (s, e) => { pantryWin = null; pantryList = null; pantryCoin = null; };
        RefreshPantry();
        pantryWin.Show();
    }

    void RefreshPantry()
    {
        if (pantryList == null) return;
        pantryCoin.Text = $"金币:{coins} 枚    饥饿 {(int)state.Hunger}/100 · 口渴 {(int)state.Thirst}/100";
        pantryList.Children.Clear();
        var items = pantryKind == ConsumeKind.Food ? Pantry.Foods : Pantry.Drinks;
        foreach (var c in items)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 6, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });

            var icon = new TextBlock { Text = c.Icon, FontSize = 18, FontFamily = new FontFamily("Segoe UI Emoji"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(icon, 0);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(new TextBlock { Text = c.Name, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12.5, FontFamily = new FontFamily("Microsoft YaHei UI") });
            var eff = new List<string>();
            if (c.Hunger > 0) eff.Add($"饥饿+{(int)c.Hunger}");
            if (c.Thirst > 0) eff.Add($"口渴+{(int)c.Thirst}");
            if (c.Happy > 0) eff.Add($"快乐+{(int)c.Happy}");
            mid.Children.Add(new TextBlock { Text = string.Join(" · ", eff), FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xAE)), FontFamily = new FontFamily("Microsoft YaHei UI") });
            Grid.SetColumn(mid, 1);

            var price = new TextBlock { Text = $"{c.Price} 金币", VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x7A)), FontSize = 12, FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(price, 2);

            var buy = new Button { Content = "投喂", Width = 52, Height = 26, IsEnabled = coins >= c.Price && taskNow == null && !sleeping, FontFamily = new FontFamily("Microsoft YaHei UI") };
            buy.Click += (s, e) => Consume(c, selfBought: false);
            Grid.SetColumn(buy, 3);

            row.Children.Add(icon); row.Children.Add(mid); row.Children.Add(price); row.Children.Add(buy);
            pantryList.Children.Add(row);
        }
    }

    // ============================== 托盘 ==============================

    void SetupTray()
    {
        tray = new WF.NotifyIcon { Text = Store.Config.petName, Visible = true };
        try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
        catch { tray.Icon = System.Drawing.SystemIcons.Application; }

        var m = new WF.ContextMenuStrip();
        m.Items.Add("显示 / 隐藏", null, (s, e) => ToggleHide());
        m.Items.Add("查看状态", null, (s, e) => ShowStatus());
        m.Items.Add("今日计划", null, (s, e) => ShowPlan());
        m.Items.Add(new WF.ToolStripSeparator());
        m.Items.Add("退出", null, (s, e) => ExitApp());
        tray.ContextMenuStrip = m;
        tray.DoubleClick += (s, e) => ToggleHide();
    }

    void HideToTray()
    {
        if (mischief.Active) EndMischief(false, silent: true);
        walking = false;
        hidden = true;
        Hide();
        try { tray.ShowBalloonTip(2500, PetName, Dialogue.TrayHide, WF.ToolTipIcon.Info); } catch { }
        Store.Log("躲进托盘(计时暂停)");
    }

    void ToggleHide()
    {
        if (hidden)
        {
            hidden = false;
            Show();
            Native.BumpTopmost(this);
            ShowBubble(Dialogue.TrayBack, 4);
            Store.Log("从托盘回到桌面");
        }
        else HideToTray();
    }

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
        var psi = new ProcessStartInfo("npm", "install")
        {
            WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "mc"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try
        {
            var p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            if (p.ExitCode == 0)
                Dispatcher.BeginInvoke(() => ShowBubble("MC模块安装成功!", 5));
            else
                Dispatcher.BeginInvoke(() => ShowBubble("安装失败:" + (err.Length > 80 ? err[..80] : err), 8));
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => ShowBubble("npm没装好,请先装Node.js", 8));
        }
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

    // ============================== 外观刷新 ==============================

    void UpdateAll() { UpdateFace(); UpdateBar(); }

    void UpdateFace()
    {
        Mood m = sleeping ? Mood.Sleep
               : taskNow != null ? Mood.Busy
               : mischief.Active ? Mood.Angry
               : DateTime.Now < happyUntil ? Mood.Happy
               : (state.Hunger < 15 || state.Thirst < 15) ? Mood.Hungry
               : (state.IsListening || listenMode) ? Mood.Music
               : state.Happy < 150 ? Mood.Angry
               : state.Happy < 450 ? Mood.Bored
               : Mood.Normal;
        ApplyFace(m);
    }

    void ApplyFace(Mood m, bool force = false)
    {
        if (m == faceNow && !force) return;
        faceNow = m;

        if (skinMode == "live2d" && l2dReady) return;                       // 模型自带动作
        if (skinMode == "custom" && skins != null) { SkinImage.Source = PickSkin(m); return; }
        if (skinMode == "gif" && gifSkins != null) { PlayGif(m); return; }

        ZzzMark.Visibility = AngryMark.Visibility = NotesMark.Visibility = SweatMark.Visibility = Visibility.Collapsed;
        BrowsAngry.Visibility = Visibility.Collapsed;
        BrowsNormal.Visibility = Visibility.Visible;
        EyesOpen.Visibility = Visibility.Visible;
        EyesClosed.Visibility = Visibility.Collapsed;
        MouthSmile.Visibility = MouthOpen.Visibility = MouthPout.Visibility = MouthAngry.Visibility = Visibility.Collapsed;
        double blush = 0.55;

        switch (m)
        {
            case Mood.Normal:
                MouthSmile.Visibility = Visibility.Visible;
                break;
            case Mood.Happy:
                EyesOpen.Visibility = Visibility.Collapsed;
                EyesClosed.Visibility = Visibility.Visible;
                MouthOpen.Visibility = Visibility.Visible;
                blush = 0.85;
                break;
            case Mood.Bored:
                MouthPout.Visibility = Visibility.Visible;
                blush = 0.35;
                break;
            case Mood.Angry:
                BrowsNormal.Visibility = Visibility.Collapsed;
                BrowsAngry.Visibility = Visibility.Visible;
                MouthAngry.Visibility = Visibility.Visible;
                AngryMark.Visibility = Visibility.Visible;
                blush = 0.25;
                break;
            case Mood.Sleep:
                EyesOpen.Visibility = Visibility.Collapsed;
                EyesClosed.Visibility = Visibility.Visible;
                MouthSmile.Visibility = Visibility.Visible;
                ZzzMark.Visibility = Visibility.Visible;
                break;
            case Mood.Music:
                EyesOpen.Visibility = Visibility.Collapsed;
                EyesClosed.Visibility = Visibility.Visible;
                MouthOpen.Visibility = Visibility.Visible;
                NotesMark.Visibility = Visibility.Visible;
                blush = 0.7;
                break;
            case Mood.Busy:
                MouthSmile.Visibility = Visibility.Visible;
                SweatMark.Visibility = Visibility.Visible;
                break;
            case Mood.Hungry:
                MouthPout.Visibility = Visibility.Visible;
                SweatMark.Visibility = Visibility.Visible;
                blush = 0.3;
                break;
        }
        BlushL.Opacity = BlushR.Opacity = blush;
    }

    void UpdateBar()
    {
        double pct = state.HappyPercent;
        BoredomFill.Width = Math.Max(0, 194 * pct);
        Brush fill = pct > 0.6 ? new SolidColorBrush(Color.FromRgb(0x7C, 0xCB, 0x7F))
                   : pct > 0.3 ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D))
                   : new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
        if (Paused) fill = new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE));
        BoredomFill.Fill = fill;

        // 暂停时:交替显示「在做什么」和「还有多久恢复计时」
        bool alt = DateTime.Now.Second % 8 < 4;
        int happy = (int)Math.Round(state.Happy);
        string need = (state.Hunger < 30 || state.Thirst < 30)
            ? (state.Hunger <= state.Thirst ? " · 饿了🍙" : " · 渴了💧") : "";
        string txt, tip = null;
        if (sleeping)
        {
            int rem = DailyPlanner.MinutesUntilSleepEnd();
            txt = alt ? "睡觉中 zzZ(计时暂停)" : $"{FmtDur(rem)}后恢复计时";
            tip = $"睡觉中,快乐值暂停计时;{FmtDur(rem)}后(07:00)醒来恢复计时";
        }
        else if (curTask != null)
        {
            var nowCst = DailyPlanner.NowCst;
            int rem = Math.Max(1, curTask.EndMinutes - (nowCst.Hour * 60 + nowCst.Minute));
            txt = alt ? $"任务中:{curTask.Name}" : $"{FmtDur(rem)}后恢复计时";
            tip = $"「{curTask.Name}」进行到 {DailyPlanner.Fmt(curTask.EndMinutes)},{FmtDur(rem)}后恢复快乐值计时";
        }
        else if (state.IsLocked)
        {
            int remM = (int)Math.Ceiling((state.LockFullUntil - DateTime.Now).TotalMinutes);
            txt = alt ? $"快乐值 1000 · 金币{coins}" : $"礼物加护 · 剩{FmtDur(remM)}";
            tip = $"礼物加护中:满快乐值保持到 {state.LockFullUntil:MM-dd HH:mm}";
        }
        else if (listenMode)
        {
            txt = alt ? $"听歌模式♪ 快乐 {happy}" : $"快乐值 {happy}/1000{need}";
            tip = "检测到你在听歌,她也跟着一起听,心情在慢慢变好~";
        }
        else
        {
            txt = $"快乐值 {happy}{need}";
        }
        BoredomText.Text = txt;
        BoredomText.ToolTip = tip;

        if (tray != null)
            tray.Text = curTask != null ? $"{PetName} · 任务:{curTask.Name}"
                     : sleeping ? $"{PetName} · 睡觉中"
                     : $"{PetName} · 快乐{happy} 饥饿{(int)state.Hunger} 口渴{(int)state.Thirst} 金币{coins}";
    }

    static string FmtDur(int minutes) =>
        minutes >= 60 ? $"{minutes / 60}小时{minutes % 60:D2}分" : $"{minutes}分钟";

    // ============================== 动画 ==============================

    void OnFrame(object s, EventArgs e)
    {
        double t = clock.Elapsed.TotalSeconds;
        double period = sleeping ? 3.8 : faceNow == Mood.Music ? 1.4 : 2.3;
        BobT.Y = (sleeping ? 2.0 : 3.2) * Math.Sin(t * 2 * Math.PI / period);
        HairSway.Angle = 1.4 * Math.Sin(t * 2 * Math.PI / period + 1.2);
        if (walking)
            JumpT.Y = -Math.Abs(Math.Sin(t * 2 * Math.PI * 2.1)) * 5;   // 走路小跳步
        else if (!bouncing)
            JumpT.Y = 0;
    }

    void Blink()
    {
        ResetBlink();
        if (skinMode != "official") return;
        if (faceNow is Mood.Sleep or Mood.Happy or Mood.Music) return;
        EyesOpen.Visibility = Visibility.Collapsed;
        EyesClosed.Visibility = Visibility.Visible;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        t.Tick += (s2, e2) => { ((DispatcherTimer)s2).Stop(); ApplyFace(faceNow, force: true); };
        t.Start();
    }

    void ResetBlink() => blinkTick.Interval = TimeSpan.FromSeconds(2.2 + rnd.NextDouble() * 3.5);

    void WaveArm()
    {
        var a = new DoubleAnimation(0, -36, TimeSpan.FromMilliseconds(240))
        { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) };
        RightArmRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, a);
    }

    bool bouncing;
    void Bounce()
    {
        var a = new DoubleAnimation(0, -22, TimeSpan.FromMilliseconds(170))
        { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new QuadraticEase(), FillBehavior = FillBehavior.Stop };
        bouncing = true;
        a.Completed += (s, e) => { JumpT.BeginAnimation(TranslateTransform.YProperty, null); bouncing = false; };
        JumpT.BeginAnimation(TranslateTransform.YProperty, a);
    }

    void ShowBubble(string text, double seconds)
    {
        if (string.IsNullOrEmpty(text) || hidden) return;
        BubbleText.Text = text;
        BubbleRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        bubbleHide.Stop();
        bubbleHide.Interval = TimeSpan.FromSeconds(seconds);
        bubbleHide.Start();
    }

    // ============================== 杂项 ==============================

    void LoadStateAndDebug()
    {
        firstRun = !File.Exists(Store.StatePath);
        var s = Store.LoadState();
        // 旧存档 boredom 是 0~100,自动迁移到 0~1000
        double happy = s.boredom <= 100 ? s.boredom * 10 : s.boredom;
        state.Happy = Math.Max(250, Math.Min(PetState.MaxHappy, happy));   // 睡醒了,精神一点
        state.Hunger = Math.Clamp(s.hunger, 0, 100);
        state.Thirst = Math.Clamp(s.thirst, 0, 100);
        state.AloneSeconds = Math.Max(0, s.aloneSeconds);
        if (DateTime.TryParse(s.lastInteraction, out var li)) state.LastInteraction = li;
        coins = Math.Max(0, s.coins);
        if (DateTime.TryParse(s.lockUntil, out var lu)) state.LockFullUntil = lu;
        foreach (var k in s.paidKeys ?? new List<string>()) paidKeys.Add(k);

        var c = Store.Config;
        if (c.debugBoredom >= 0)
        {
            state.Happy = Math.Min(PetState.MaxHappy, c.debugBoredom);
            mischiefCooldownUntil = DateTime.Now.AddSeconds(5);
            Store.Log($"[debug] 快乐值强制为 {state.Happy}");
        }
        if (c.debugAloneSeconds >= 0)
        {
            state.AloneSeconds = c.debugAloneSeconds;
            mischiefCooldownUntil = DateTime.Now.AddSeconds(5);
            Store.Log($"[debug] 独处秒数强制为 {state.AloneSeconds}");
        }
        if (c.debugHunger >= 0) { state.Hunger = Math.Clamp(c.debugHunger, 0, 100); Store.Log($"[debug] 饥饿值强制为 {state.Hunger}"); }
        if (c.debugThirst >= 0) { state.Thirst = Math.Clamp(c.debugThirst, 0, 100); Store.Log($"[debug] 口渴值强制为 {state.Thirst}"); }
        if (c.debugCoins >= 0)
        {
            coins = c.debugCoins;
            Store.Log($"[debug] 金币强制为 {coins}");
        }
        if (s.winX > -90000 && s.winY > -90000)
        {
            Left = s.winX; Top = s.winY;
            posRestored = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
    }

    void PlaceWindow()
    {
        if (posRestored) { ClampToScreen(); return; }
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 36;
        Top = wa.Bottom - Height - 4;
    }

    void ClampToScreen()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
        Left = Math.Min(Math.Max(Left, l - Width * 0.3), r - Width * 0.7);
        Top = Math.Min(Math.Max(Top, t - 8), b - Height * 0.5);
    }

    void Persist()
    {
        Store.SaveState(new SavedState
        {
            boredom = state.Happy,
            hunger = state.Hunger,
            thirst = state.Thirst,
            aloneSeconds = state.AloneSeconds,
            lastInteraction = state.LastInteraction.ToString("o"),
            winX = Left,
            winY = Top,
            coins = coins,
            lockUntil = state.LockFullUntil == DateTime.MinValue ? "" : state.LockFullUntil.ToString("o"),
            paidKeys = paidKeys.ToList(),
        });
    }

    void LogShotRect()
    {
        try
        {
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget == null) return;
            double sc = src.CompositionTarget.TransformToDevice.M11;
            var tl = PointToScreen(new Point(0, 0));
            Store.Log($"SHOT {(int)tl.X},{(int)tl.Y},{(int)(Width * sc)},{(int)(Height * sc)}");
        }
        catch { }
    }

    
    void LoadGifs()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "gif");
            if (!Directory.Exists(dir)) { gifSkins = null; return; }
            string idle = Path.Combine(dir, "idle.gif");
            if (!File.Exists(idle)) { gifSkins = null; return; }
            gifSkins = new Dictionary<string, GifAnimation>();
            foreach (var k in new[] { "idle", "happy", "bored", "angry", "sleep", "music", "busy", "hungry" })
            {
                string p = Path.Combine(dir, k + ".gif");
                if (!File.Exists(p)) continue;
                var anim = GifLoader.LoadGif(p, Store.Config.blackToTransparent);
                if (anim.Frames.Count > 0) gifSkins[k] = anim;
            }
            Store.Log("已读取 GIF 皮肤 gif/*.gif");
        }
        catch (Exception ex) { gifSkins = null; Store.Log("GIF 皮肤加载失败:" + ex.Message); }
    }

    void LoadSkins()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "skin");
            string idle = Path.Combine(dir, "idle.png");
            if (!File.Exists(idle)) { skins = null; return; }
            skins = new Dictionary<string, BitmapImage>();
            foreach (var k in new[] { "idle", "happy", "bored", "angry", "sleep", "music", "busy", "hungry" })
            {
                string p = Path.Combine(dir, k + ".png");
                if (!File.Exists(p)) continue;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(p);
                bi.EndInit();
                bi.Freeze();
                skins[k] = bi;
            }
            Store.Log("已读取自定义皮肤 skin/*.png");
        }
        catch (Exception ex) { skins = null; Store.Log("皮肤加载失败:" + ex.Message); }
    }

    // ============================== 外观三模式 ==============================

    /// <summary>切换外观:official=官方矢量 / custom=PNG皮肤 / live2d=Live2D模型。</summary>
    void ApplyMode(string mode, bool announce)
    {
        if (mode == "auto") mode = skins != null ? "custom" : "official";

        if (mode == "gif" && gifSkins == null)
        {
            LoadGifs();
            if (gifSkins == null)
            {
                ShowBubble("没找到 GIF 皮肤哦~\n把 idle.gif 等放进 exe 旁的 gif 文件夹再试!", 9);
                return;
            }
        }
        if (mode == "custom" && skins == null)
        {
            LoadSkins(); LoadGifs();
            if (skins == null)
            {
                ShowBubble(Dialogue.NoCustomSkin, 9);
                return;
            }
        }

        skinMode = mode;
        Store.Config.skinMode = mode;
        Store.SaveConfig();
        Dialogue.SetMode(mode);

        switch (mode)
        {
            case "official":
                HideLive2D();
                SkinImage.Visibility = Visibility.Collapsed;
                VectorPet.Visibility = Visibility.Visible;
                if (announce) ShowBubble(Dialogue.SwitchOfficial, 5);
                break;
            case "custom":
                HideLive2D();
                VectorPet.Visibility = Visibility.Collapsed;
                SkinImage.Visibility = Visibility.Visible;
                if (announce) ShowBubble(Dialogue.SwitchCustom, 5);
                break;
            case "gif":
                HideLive2D();
                VectorPet.Visibility = Visibility.Collapsed;
                SkinImage.Visibility = Visibility.Visible;
                if (announce) ShowBubble(Dialogue.SwitchCustom, 5);
                break;
            case "live2d":
                // 先保持当前形象,加载成功后再切换显示
                StartLive2D(announce);
                break;
        }
        ApplyFace(faceNow, force: true);
        Store.Log($"外观切换为 {mode}");
    }

    void HideLive2D()
    {
        l2dReady = false;
        if (l2d != null)
        {
            try { l2d.CoreWebView2?.Navigate("about:blank"); } catch { }
            l2d.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>在 live2d 文件夹中找模型:*.model3.json(Cubism4)或 *.model.json(Cubism2),或 model_url.txt 里的网址。</summary>
    static string FindLive2DModel(string dir, out string why)
    {
        why = "";
        try
        {
            var m3 = Directory.EnumerateFiles(dir, "*.model3.json", SearchOption.AllDirectories).FirstOrDefault()
                  ?? Directory.EnumerateFiles(dir, "*.model.json", SearchOption.AllDirectories).FirstOrDefault();
            if (m3 != null)
                return Path.GetRelativePath(dir, m3).Replace('\\', '/');
            string urlFile = Path.Combine(dir, "model_url.txt");
            if (File.Exists(urlFile))
            {
                string url = File.ReadAllText(urlFile).Trim();
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
            }
            why = $"把模型(含 .model3.json)放进 {dir}";
        }
        catch (Exception ex) { why = ex.Message; }
        return null;
    }

    async void StartLive2D(bool announce)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "live2d");
        try { Directory.CreateDirectory(dir); } catch { }
        string model = FindLive2DModel(dir, out string why);
        if (model == null)
        {
            ShowBubble(Dialogue.Live2dNotFound(why), 9);
            skinMode = skins != null ? "custom" : "official";
            Dialogue.SetMode(skinMode);
            return;
        }

        try
        {
            if (l2d == null)
            {
                l2d = new Microsoft.Web.WebView2.Wpf.WebView2
                {
                    DefaultBackgroundColor = System.Drawing.Color.Transparent,
                    Width = 220, Height = 272,
                };
                PetRoot.Children.Add(l2d);
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    userDataFolder: Path.Combine(Store.Dir, "webview2"));
                await l2d.EnsureCoreWebView2Async(env);
                l2d.CoreWebView2.SetVirtualHostNameToFolderMapping("pet.live2d", dir,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                l2d.CoreWebView2.DocumentTitleChanged += (s, e) =>
                {
                    string t = l2d.CoreWebView2.DocumentTitle ?? "";
                    if (t == "OK" && skinMode == "live2d")
                    {
                        l2dReady = true;
                        VectorPet.Visibility = Visibility.Collapsed;
                        SkinImage.Visibility = Visibility.Collapsed;
                        l2d.Visibility = Visibility.Visible;
                        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                        Native.MakeChildrenClickThrough(hwnd);   // 让模型层不挡拖拽/右键
                        Store.Log("Live2D 模型加载成功");
                        ShowBubble(Dialogue.SwitchLive2d, 5);
                    }
                    else if (t.StartsWith("ERR"))
                    {
                        Store.Log("Live2D 加载失败:" + t);
                        ShowBubble(Dialogue.Live2dFail, 9);
                    }
                };
            }
            WriteLive2DHostHtml(dir, model);
            l2dReady = false;
            l2d.Visibility = Visibility.Visible;
            l2d.CoreWebView2.Navigate("https://pet.live2d/_host.html");
            if (announce) ShowBubble(Dialogue.Live2dLoading, 5);
            Store.Log($"开始加载 Live2D:{model}");
        }
        catch (Exception ex)
        {
            Store.Log("Live2D 初始化失败:" + ex.Message);
            ShowBubble(Dialogue.Live2dNoWebView, 9);
            skinMode = skins != null ? "custom" : "official";
            Dialogue.SetMode(skinMode);
        }
    }

    static void WriteLive2DHostHtml(string dir, string modelRef)
    {
        string html = """
<!DOCTYPE html><html><head><meta charset="utf-8">
<style>html,body{margin:0;padding:0;width:100%;height:100%;background:transparent;overflow:hidden}canvas{display:block}</style>
</head><body>
<script>
function load(list){return list.reduce((p,s)=>p.catch(()=>new Promise((ok,bad)=>{var t=document.createElement('script');t.src=s;t.onload=ok;t.onerror=bad;document.head.appendChild(t);})),Promise.reject());}
function fit(model,app){
  var W=app.renderer.width, H=app.renderer.height;
  var s=Math.min(W/model.width, H/model.height)*0.95;
  model.scale.set(s);
  model.anchor.set(0.5,0.5);
  model.position.set(W/2, H/2);
}
(async()=>{
  try{await load(['libs/live2dcubismcore.min.js','https://cubism.live2d.com/sdk-web/cubismcore/live2dcubismcore.min.js']);}catch(e){}
  try{await load(['libs/live2d.min.js','https://cdn.jsdelivr.net/gh/dylanNew/live2d/webgl/Live2D/lib/live2d.min.js']);}catch(e){}
  try{await load(['libs/pixi.min.js','https://cdn.jsdelivr.net/npm/pixi.js@6.5.10/dist/browser/pixi.min.js']);}catch(e){document.title='ERR:pixi';return;}
  try{await load(['libs/index.min.js','https://cdn.jsdelivr.net/npm/pixi-live2d-display@0.4.0/dist/index.min.js']);}catch(e){document.title='ERR:plugin';return;}
  try{
    const app=new PIXI.Application({backgroundAlpha:0,resizeTo:window,antialias:true,autoStart:true});
    document.body.appendChild(app.view);
    const model=await PIXI.live2d.Live2DModel.from('MODEL_REF');
    app.stage.addChild(model);
    fit(model,app);
    window.addEventListener('resize',()=>fit(model,app));
    document.title='OK';
  }catch(e){document.title='ERR:'+(e&&e.message?e.message:e);}
})();
</script></body></html>
""";
        html = html.Replace("MODEL_REF", modelRef);
        try { File.WriteAllText(Path.Combine(dir, "_host.html"), html, System.Text.Encoding.UTF8); } catch { }
    }

    ImageSource PickSkin(Mood m)
    {
        string k = m switch
        {
            Mood.Happy => "happy",
            Mood.Bored => "bored",
            Mood.Angry => "angry",
            Mood.Sleep => "sleep",
            Mood.Music => "music",
            Mood.Busy => "busy",
            Mood.Hungry => "hungry",
            _ => "idle"
        };
        return skins.TryGetValue(k, out var b) ? b : skins["idle"];
    }

    void PlayGif(Mood m)
    {
        string k = m switch
        {
            Mood.Happy => "happy",
            Mood.Bored => "bored",
            Mood.Angry => "angry",
            Mood.Sleep => "sleep",
            Mood.Music => "music",
            Mood.Busy => "busy",
            Mood.Hungry => "hungry",
            _ => "idle"
        };
        if (gifSkins == null || !gifSkins.TryGetValue(k, out var anim))
        {
            if (gifSkins != null && gifSkins.TryGetValue("idle", out var idleAnim)) anim = idleAnim;
            else return;
        }
        if (curGifAnim == anim) return;
        curGifAnim = anim;
        gifFrameIndex = 0;
        gifTimer?.Stop();
        if (anim.Frames.Count == 0) return;
        SkinImage.Source = anim.Frames[0];
        if (anim.Frames.Count > 1)
        {
            gifTimer = new DispatcherTimer();
            gifTimer.Interval = TimeSpan.FromMilliseconds(anim.Delays[0]);
            gifTimer.Tick += (s, e) =>
            {
                gifFrameIndex = (gifFrameIndex + 1) % anim.Frames.Count;
                SkinImage.Source = anim.Frames[gifFrameIndex];
                gifTimer.Interval = TimeSpan.FromMilliseconds(anim.Delays[gifFrameIndex]);
            };
            gifTimer.Start();
        }
    }

    void ApplyScale(double scale)
    {
        scale = Math.Clamp(scale, 0.5, 3.0);
        Store.Config.petScale = scale;
        Store.SaveConfig();
        PetRoot.LayoutTransform = new ScaleTransform(scale, scale);
    }
}
