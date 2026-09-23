namespace XinHaiHai;

/// <summary>
/// 台词入口:优先从 DialogueDB(data\lines_*.json,按当前外观模式)随机抽取,
/// JSON 缺失对应类别时回退到内置丛雨风台词,保证永远有话说。
/// </summary>
public static class Dialogue
{
    static readonly Random R = new();
    static string Pick(params string[] a) => a[R.Next(a.Length)];

    /// <summary>桌宠名字(可在右键菜单更改)。</summary>
    public static string Name => Store.Config.petName;

    /// <summary>外观切换时调用:加载对应形象的台词包(official/custom=丛雨风,live2d=初音风)。</summary>
    public static void SetMode(string mode) => DialogueDB.SetPersona(mode);

    public static string Welcome => DialogueDB.Pick("welcome") ?? Pick(
        $"主人!{Name}参上~今天也要好好陪吾哦!",
        "唔…主人来啦?哼、才没有一直在等你呢!");

    public static string FirstRun => DialogueDB.Pick("firstRun") ??
        $"初次见面,吾是{Name}!\n拖着吾可以散步,右键有互动、投喂、商店,\n头顶那条是吾的快乐值,别让它见底哦~";

    // —— 主动撒娇(按快乐值分层) ——
    public static string SassyContent => DialogueDB.Pick("sassyContent") ?? Pick(
        "嘿嘿~和主人待在一起,心情很好哦♪",
        "本座今天状态绝佳!要看吾转圈圈吗?");

    public static string SassySlight => DialogueDB.Pick("sassySlight") ?? Pick(
        "那个……主人?忙完了的话,陪吾玩一会儿嘛……",
        "主人~ 摸摸头,就摸一下,一下就好!");

    public static string SassyBored => DialogueDB.Pick("sassyBored") ?? Pick(
        "喂——!本座要无聊死啦!理理吾嘛!",
        "再不回来,本座可要对你的鼠标下手了哦?!");

    public static string SassyCritical => DialogueDB.Pick("sassyCritical") ?? Pick(
        "……最后警告哦,主人。鼠、标、要、看、好。",
        "哼!既然主人不理吾,那就别怪本座出手了!!");

    public static string SassyListening => DialogueDB.Pick("sassyListening") ?? Pick(
        "♪~♪~ 这首歌不错呢……才、才不是替代主人呢!",
        "哼哼~ 音乐可比某个不理人的主人贴心多了!");

    // —— 互动反馈 ——
    public static string Pat => DialogueDB.Pick("pat") ?? Pick(
        "嘿嘿……主人的手,暖暖的……",
        "唔……就、就再摸一会儿也不是不行啦!");

    public static string Play => DialogueDB.Pick("play") ?? Pick(
        "哇哈!好玩好玩!主人再来一次!",
        "嘿嘿嘿~本座就知道,主人最喜欢吾了!");

    public static string Chat => DialogueDB.Pick("chat") ?? Pick(
        "主人今天过得怎么样?有好好吃饭吗?吾可是一直看着你哦~",
        "呐,主人,忙完这阵就多陪陪吾嘛……拉钩,约好了!");

    public static string DragWalk => DialogueDB.Pick("dragWalk") ?? Pick(
        "哇哇——!放、放吾下来……唔,算了,散步的感觉也不坏啦。",
        "要去哪里呀主人?吾跟你一起去!");

    public static string DragShort => DialogueDB.Pick("dragShort") ?? Pick(
        "呀!别突然拽吾啦!",
        "唔?要挪个位置吗?好吧好吧。");

    // —— 自主行为 ——
    public static string MusicGo(string player) => player == null
        ? (DialogueDB.Pick("musicGoNoPlayer") ?? "哼,主人不陪吾,想自己放首歌……结果播放器都打不开,呜呜。")
        : (DialogueDB.Pick("musicGo", ("player", player)) ?? $"没人陪……本座自己去听歌了!哼!\n(已打开 {player},这就开始放歌~♪)");

    public static string MischiefStart => DialogueDB.Pick("mischiefStart") ?? Pick(
        "哼——!鼠标借本座玩一下!想要回去就来抓吾呀!",
        "嘿嘿嘿,鼠标到手啦~想让吾放手,就过来陪吾!");

    public static string MischiefCaught => DialogueDB.Pick("mischiefCaught") ?? Pick(
        "哇!被、被抓到了……哼,算主人有诚意,原谅你一下下!",
        "嘿嘿,主人终于肯理吾了……那就休战!");

    public static string MischiefGiveup => DialogueDB.Pick("mischiefGiveup") ?? Pick(
        "哼!算你跑得快……下次可没这么好躲了!",
        "呜……连鼠标都追不到……本座、本座才没有沮丧!");

    // —— 作息 ——
    public static string TaskStart(string t) => DialogueDB.Pick("taskStart", ("task", t)) ?? Pick(
        $"到点啦!本座要开始「{t}」了,先不聊咯~",
        $"「{t}」时间到!吾可是很守规矩的!");

    public static string TaskDone(string t) => DialogueDB.Pick("taskDone", ("task", t)) ?? Pick(
        $"呼——「{t}」完成!快夸夸吾!",
        $"「{t}」结束啦~接下来到自由时间了!");

    public static string TaskRefuse(string t) => DialogueDB.Pick("taskRefuse", ("task", t)) ?? Pick(
        $"唔,现在是「{t}」时间!等吾忙完再来找吾嘛!",
        $"不行不行,「{t}」还没做完呢,主人稍等一下下!");

    public static string PlanMade(string desc) => DialogueDB.Pick("planMade", ("desc", desc)) ??
        $"凌晨零点,本座做好今日规划啦!\n{desc}\n其余时间……都可以陪主人哦!";

    public static string GoodNight => DialogueDB.Pick("goodNight") ?? Pick(
        "呼啊……到吾睡觉的时间了,主人也早点休息哦……晚安~",
        "眼皮打架了……本座先睡啦,梦里见,主人~");

    public static string GoodMorning => DialogueDB.Pick("goodMorning") ?? Pick(
        "唔哇,早上好主人!新的一天也请多多陪伴吾!",
        "呼啊~睡得真好!主人,早安!");

    public static string SleepPoke => DialogueDB.Pick("sleepPoke") ?? Pick(
        "唔呣……再睡五分钟……",
        "呜……明天再玩……晚安,主人……");

    public static string TrayHide => DialogueDB.Pick("trayHide") ??
        "吾到托盘里休息啦,想吾了就双击图标~";

    public static string TrayBack => DialogueDB.Pick("trayBack") ?? Pick(
        "哇,主人来找吾啦!嘿嘿~",
        "咻——本座回来了!");

    // —— 走动 ——
    public static string WalkStart => DialogueDB.Pick("walkStart") ?? Pick(
        "唔,坐久了……出去溜达溜达!",
        "走走走,活动一下筋骨~");

    public static string WalkDone => DialogueDB.Pick("walkDone") ?? Pick(
        "呼,到啦!这里视野不错嘛~",
        "嘿嘿,散步真舒服。");

    // —— 音乐停止 ——
    public static string MusicStopByCompany => DialogueDB.Pick("musicStopByCompany") ?? Pick(
        "主人来了!那吾就不听歌啦,歌可比不上主人~",
        "嘿嘿,主人回来了,音乐关掉关掉!");

    public static string MusicStopByBored => DialogueDB.Pick("musicStopByBored") ?? Pick(
        "听够了……果然还是想要主人陪。",
        "哼,一个人听歌好没意思,关掉了。");

    // —— 工作与商店 ——
    public static string WorkDone(string task, int pay, int total) =>
        DialogueDB.Pick("workDone", ("task", task), ("pay", pay.ToString()), ("total", total.ToString())) ?? Pick(
        $"「{task}」完工!赚到 {pay} 金币,现在攒了 {total} 枚~快去商店给吾买礼物!",
        $"呼——「{task}」做完了!工钱 {pay} 金币入账,一共 {total} 枚了哦!");

    public static string GiftBusy => DialogueDB.Pick("giftBusy") ??
        "现在在忙,礼物先帮吾收着,忙完再拆!";

    public static string GiftSleep => DialogueDB.Pick("giftSleep") ??
        "Zzz……(睡熟了,礼物明天再送吧)";

    public static string NoCoins => DialogueDB.Pick("noCoins") ?? Pick(
        "金币不够啦……让吾多做几天工作攒一攒吧!",
        "呜,买不起……主人,吾会努力打工的!");

    // —— 饥饿 / 口渴 / 听歌模式 / 自主进食 ——
    public static string Hungry => DialogueDB.Pick("hungry") ?? Pick(
        "呜……肚子饿扁了,主人给吾做点吃的嘛……",
        "咕噜噜~ 吾的肚子在抗议了!要吃东西!");

    public static string Thirsty => DialogueDB.Pick("thirsty") ?? Pick(
        "喉咙好干……主人,吾想喝点东西……",
        "渴——!给吾倒杯水好不好嘛~");

    public static string ListenModeOn => DialogueDB.Pick("listenModeOn") ?? Pick(
        "咦,主人在放歌?那吾也一起听~心情变好了♪",
        "有音乐诶!吾要跟着一起摇摆啦♪");

    public static string NoCoinsFood => DialogueDB.Pick("noCoinsFood") ?? Pick(
        "肚子好饿,可是……金币一枚都没有了,呜呜。",
        "想吃东西,但买不起……主人快去打工赚钱嘛!");

    public static string SelfBuyPrefix(string icon, string itemName) =>
        DialogueDB.Pick("selfBuyPrefix", ("icon", icon), ("name", itemName)) ??
        $"(没人投喂,自己买了{icon}{itemName})\n";

    // —— 物品台词(foods / drinks / gifts,每个物品 3 条随机) ——
    public static string FoodLine(string key, string fallback) => DialogueDB.PickItem("foods", key) ?? fallback;
    public static string DrinkLine(string key, string fallback) => DialogueDB.PickItem("drinks", key) ?? fallback;
    public static string GiftLine(string key, string fallback) => DialogueDB.PickItem("gifts", key) ?? fallback;

    /// <summary>吃/喝的台词(自动按食物或饮料查表,查不到用物品自带的一句兜底)。</summary>
    public static string FoodLine(Consumable c) =>
        DialogueDB.PickItem(c.Kind == ConsumeKind.Food ? "foods" : "drinks", c.Key) ?? c.Line;

    /// <summary>收到礼物的台词。</summary>
    public static string GiftLine(Gift g) => DialogueDB.PickItem("gifts", g.Key) ?? g.Line;

    /// <summary>自主进食的前缀说明。</summary>
    public static string SelfBuyPrefix(Consumable c) => SelfBuyPrefix(c.Icon, c.Name);

    // —— 外观与改名 ——
    public static string Rename(string newName, string oldName) =>
        DialogueDB.Pick("rename", ("name", newName), ("old", oldName)) ??
        $"从今天起,吾就是「{newName}」啦!\n(原来的名字「{oldName}」也会记在心里的~)";

    public static string NoCustomSkin => DialogueDB.Pick("noCustomSkin") ??
        "没找到自定义皮肤哦~\n把 idle.png 等放进 exe 旁的 skin 文件夹再试!";

    public static string SwitchOfficial => DialogueDB.Pick("switchOfficial") ?? "变回官方形象啦!还是这身最习惯~";
    public static string SwitchCustom => DialogueDB.Pick("switchCustom") ?? "换上主人给的新衣服!好看吗?";
    public static string SwitchLive2d => DialogueDB.Pick("switchLive2d") ?? "Live2D 形象,登场!";
    public static string Live2dLoading => DialogueDB.Pick("live2dLoading") ?? "正在加载 Live2D 模型…";
    public static string Live2dNotFound(string why) =>
        DialogueDB.Pick("live2dNotFound", ("why", why)) ?? $"没找到 Live2D 模型…\n{why}";
    public static string Live2dFail => DialogueDB.Pick("live2dFail") ??
        "Live2D 加载失败了…检查模型文件或网络(首次需联网拉取运行库)";
    public static string Live2dNoWebView => DialogueDB.Pick("live2dNoWebView") ??
        "这台电脑好像没有 WebView2 运行时,用不了 Live2D 模式…";
}
