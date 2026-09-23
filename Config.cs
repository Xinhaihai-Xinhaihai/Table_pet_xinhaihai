using System.IO;
using System.Text.Json;

namespace XinHaiHai;

/// <summary>用户可编辑的配置(%APPDATA%\心海海\config.json)。</summary>
public class AppConfig
{
    /// <summary>桌宠的名字(右键菜单可改)。</summary>
    public string petName { get; set; } = "心海海";
    /// <summary>外观模式:auto=有自定义皮肤就用/否则官方;official=官方矢量;custom=PNG皮肤;live2d=Live2D模型。</summary>
    public string skinMode { get; set; } = "auto";
    /// <summary>桌宠缩放比例(0.5 ~ 3.0)。</summary>
    public double petScale { get; set; } = 1.0;
    /// <summary>是否自动将 GIF/PNG 黑色背景(#000000)处理为透明背景。</summary>
    public bool blackToTransparent { get; set; } = true;


    // ============================== 对话模式 ==============================
    /// <summary>对话模式:local=本地 JSON 台词随机抽取;llm=接入大模型 API,按人设即时生成。</summary>
    public string dialogueMode { get; set; } = "local";
    /// <summary>大模型接口(OpenAI 兼容)基址,末尾到 /v1 即可,如 https://api.openai.com/v1 。</summary>
    public string llmBaseUrl { get; set; } = "https://api.openai.com/v1";
    /// <summary>大模型 API Key(留空则即使选了 llm 模式也回退本地台词)。</summary>
    public string llmApiKey { get; set; } = "";
    /// <summary>大模型型号名,如 gpt-4o-mini。</summary>
    public string llmModel { get; set; } = "gpt-4o-mini";
    /// <summary>人设提示词(可自由修改):决定桌宠在 llm 模式下的说话风格与身份。</summary>
    public string llmPersona { get; set; } =
        "你是名为「{name}」的桌面宠物,原型是《千恋＊万花》里的付丧神丛雨。" +
        "自称「吾」或「本座」,称呼使用者为「主人」,性格傲娇、古风、爱撒娇,活了五百多年却常有孩子气的一面。" +
        "请始终只用简体中文回应,每次只说一句话,30 字以内,自然可爱,不要加引号、括号或旁白,不要解释。";
    /// <summary>大模型响应超时秒数,超过则取消本次对话台词(不影响快乐值增加等其他功能)。</summary>
    public int llmTimeoutSeconds { get; set; } = 6;
    /// <summary>大模型采样温度(0~2):越高越随机活泼,越低越稳定。</summary>
    public double llmTemperature { get; set; } = 0.9;
    /// <summary>自定义听歌软件的完整路径或网址;留空则自动探测常见音乐软件。</summary>
    public string musicCommand { get; set; } = "";
    /// <summary>打开听歌软件后是否自动按下媒体“播放”键,真正开始放歌。</summary>
    public bool musicAutoPlay { get; set; } = true;
    /// <summary>播放器冷启动后等待几秒再按“播放”键(给它加载时间)。</summary>
    public int musicPlayDelaySeconds { get; set; } = 12;
    /// <summary>无聊值见底后是否允许她拖拽鼠标求陪伴。</summary>
    public bool mischiefEnabled { get; set; } = true;
    /// <summary>是否允许她在快乐值可以缓慢上升时,检测使用者听歌并切入“听歌模式”。</summary>
    public bool listenModeEnabled { get; set; } = true;
    /// <summary>饥饿或口渴低于该阈值且没人投喂时,她会自己买东西吃(金币允许时)。</summary>
    public double autoEatThreshold { get; set; } = 30;
    /// <summary>调试用:时间加速倍率,只影响快乐值/独处/饥饿/口渴的流逝速度。</summary>
    public double timeScale { get; set; } = 1.0;
    /// <summary>调试用:启动时强制指定快乐值(0~1000),-1 表示不启用。</summary>
    public double debugBoredom { get; set; } = -1;
    /// <summary>调试用:启动时强制指定已独处秒数,-1 表示不启用。</summary>
    public double debugAloneSeconds { get; set; } = -1;
    /// <summary>调试用:启动时强制饥饿值(0~100),-1 表示不启用。</summary>
    public double debugHunger { get; set; } = -1;
    /// <summary>调试用:启动时强制口渴值(0~100),-1 表示不启用。</summary>
    public double debugThirst { get; set; } = -1;
    /// <summary>调试用:忽略作息/睡觉(让音乐与恶作剧逻辑可被立即测试)。</summary>
    public bool debugIgnoreSchedule { get; set; } = false;
    /// <summary>调试用:每秒走动触发概率(0~1),-1 表示用内置默认。</summary>
    public double debugWalkChance { get; set; } = -1;
    /// <summary>调试用:启动时直接打开礼物商店窗口。</summary>
    public bool debugOpenShop { get; set; } = false;
    /// <summary>调试用:启动时直接打开状态窗口。</summary>
    public bool debugOpenStatus { get; set; } = false;
    /// <summary>调试用:启动时直接打开食堂窗口。</summary>
    public bool debugOpenPantry { get; set; } = false;
    /// <summary>调试用:启动时强制金币数,-1 表示不启用。</summary>
    public int debugCoins { get; set; } = -1;
    /// <summary>调试用:假设有听歌软件在运行(配合真实声音测试听歌模式)。</summary>
    public bool debugAssumePlayer { get; set; } = false;

    /// <summary>MC 服务器地址。本机开服填 127.0.0.1，Sakura 穿透也填本机。</summary>
    public string mcHost { get; set; } = "127.0.0.1";
    /// <summary>MC 端口，默认 25565。</summary>
    public int mcPort { get; set; } = 25565;
}

/// <summary>跨启动保存的桌宠状态。</summary>
public class SavedState
{
    public double boredom { get; set; } = 850;               // 兼容旧字段名:实为快乐值(0~1000)
    public double hunger { get; set; } = 100;
    public double thirst { get; set; } = 100;
    public double aloneSeconds { get; set; } = 0;
    public string lastInteraction { get; set; } = "";
    public double winX { get; set; } = -99999;
    public double winY { get; set; } = -99999;
    public int coins { get; set; } = 100;                      // 金币
    public string lockUntil { get; set; } = "";              // 礼物加护截止时间
    public List<string> paidKeys { get; set; } = new();      // 已发过工资的工作任务
}

public static class Store
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "心海海");

    static readonly JsonSerializerOptions JOpt = new() { WriteIndented = true };
    static readonly object LogLock = new();

    public static AppConfig Config { get; private set; } = new();

    public static string StatePath => Path.Combine(Dir, "state.json");

    public static void Init()
    {
        Directory.CreateDirectory(Dir);
        string cfgPath = Path.Combine(Dir, "config.json");
        try
        {
            if (File.Exists(cfgPath))
                Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(cfgPath)) ?? new AppConfig();
        }
        catch { Config = new AppConfig(); }
        if (string.IsNullOrWhiteSpace(Config.petName)) Config.petName = "心海海";
        SaveConfig();
    }

    /// <summary>把当前配置写回磁盘(改名/切换外观后调用)。</summary>
    public static void SaveConfig()
    {
        try { File.WriteAllText(Path.Combine(Dir, "config.json"), JsonSerializer.Serialize(Config, JOpt)); } catch { }
    }

    public static SavedState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<SavedState>(File.ReadAllText(StatePath)) ?? new SavedState();
        }
        catch { }
        return new SavedState();
    }

    public static void SaveState(SavedState s)
    {
        try { File.WriteAllText(StatePath, JsonSerializer.Serialize(s, JOpt)); } catch { }
    }

    public static void Log(string msg)
    {
        try
        {
            lock (LogLock)
                File.AppendAllText(Path.Combine(Dir, "log.txt"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\r\n");
        }
        catch { }
    }
}
