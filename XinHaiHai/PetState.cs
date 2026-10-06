namespace XinHaiHai;

public enum Mood { Normal, Happy, Bored, Angry, Sleep, Music, Busy, Hungry }

/// <summary>
/// 心海海的情绪与生理内核。
/// 快乐值:0~1000,1000=心满意足,0=无聊到极点;满值→0 恰好 3 小时(未加速)。
/// 饥饿值/口渴值:各 0~100,随时间下降(睡觉也掉),靠投喂/自主进食恢复。
/// 听歌模式:检测到听歌软件在放,快乐值缓慢上升、独处计时冻结。
/// </summary>
public class PetState
{
    public const double MaxHappy = 1000;
    public const double FullToZeroSeconds = 3 * 3600;     // 快乐值满→0 三小时
    public const double ListenFillSeconds = 60 * 60;      // 听歌模式:约 1 小时从 0 涨满
    public const double HungerZeroSeconds = 14 * 3600;    // 饥饿:14 小时掉光
    public const double ThirstZeroSeconds = 10 * 3600;    // 口渴:10 小时掉光

    public double Happy = 850;
    public double Hunger = 100;
    public double Thirst = 100;
    public double AloneSeconds = 0;
    public DateTime LastInteraction = DateTime.Now;

    public bool MusicFiredThisCycle = false;
    public double MusicTriggerAt;                       // 独处多少秒后自己去听歌(1~2h随机)
    public DateTime ListeningUntil = DateTime.MinValue; // 她自己放歌的持续到几点
    public DateTime LockFullUntil = DateTime.MinValue;  // 礼物加护:此前保持满快乐值

    static readonly Random R = new();

    public PetState() => MusicTriggerAt = RandomMusicAt();

    static double RandomMusicAt() => 3600 + R.NextDouble() * 3600;

    public bool IsListening => DateTime.Now < ListeningUntil;
    public bool IsLocked => DateTime.Now < LockFullUntil;

    /// <summary>
    /// 每秒推进。
    /// paused=任务/睡觉/托盘时冻结快乐值;listening=听歌模式(缓慢加快乐、独处冻结);
    /// selfMusic=她自己放的歌正在播:快乐值消耗略微减缓 4%。
    /// 饥饿与口渴无论如何都会下降(含睡觉)。
    /// </summary>
    public void Tick(double dt, bool paused, bool listening, bool selfMusic)
    {
        double scaled = dt * Math.Max(0.01, Store.Config.timeScale);

        // 生理需求:任何时候都在消耗
        Hunger = Math.Max(0, Hunger - scaled * 100.0 / HungerZeroSeconds);
        Thirst = Math.Max(0, Thirst - scaled * 100.0 / ThirstZeroSeconds);

        if (IsLocked)   // 礼物加护:视同一直被陪伴
        {
            Happy = MaxHappy;
            AloneSeconds = 0;
            return;
        }
        if (paused) return;

        if (listening)  // 听歌模式:缓慢变开心,独处计时冻结
        {
            Happy = Math.Min(MaxHappy, Happy + scaled * MaxHappy / ListenFillSeconds);
            return;
        }

        double drain = scaled * MaxHappy / FullToZeroSeconds;
        if (selfMusic) drain *= 0.96;   // 自己在听歌,心情消耗慢 4%
        Happy = Math.Max(0, Happy - drain);
        AloneSeconds += scaled;
    }

    /// <summary>被陪伴了:增加快乐值、清零独处计时。</summary>
    public void Interact(double amount)
    {
        Happy = Math.Min(MaxHappy, Happy + amount);
        AloneSeconds = 0;
        LastInteraction = DateTime.Now;
        MusicFiredThisCycle = false;
        MusicTriggerAt = RandomMusicAt();
    }

    /// <summary>吃东西/喝东西:恢复饥饿、口渴,附带一点快乐。</summary>
    public void Feed(double hunger, double thirst, double happy)
    {
        Hunger = Math.Min(100, Hunger + hunger);
        Thirst = Math.Min(100, Thirst + thirst);
        Happy = Math.Min(MaxHappy, Happy + happy);
    }

    /// <summary>一次听歌结束后重新武装:再独处 25~45 分钟就可以再听一次(一个周期内可多次听歌)。</summary>
    public void RearmMusic()
    {
        MusicFiredThisCycle = false;
        MusicTriggerAt = AloneSeconds + 1500 + R.NextDouble() * 1200;   // +25~45 分钟
    }

    public bool ShouldStartMusic() => !MusicFiredThisCycle && AloneSeconds >= MusicTriggerAt;

    public bool WantsMischief() => Happy <= 0 && AloneSeconds >= FullToZeroSeconds;

    public double HappyPercent => Happy / MaxHappy;
}
