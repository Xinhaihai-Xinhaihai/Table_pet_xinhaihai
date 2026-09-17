using System.IO;
using System.Text;
using System.Text.Json;

namespace XinHaiHai;

public class ScheduledTask
{
    public string Name { get; set; }
    public int StartMinutes { get; set; }   // 距当天 0:00 的分钟数
    public int DurationMinutes { get; set; }
    public bool IsWork { get; set; }        // 工作任务:完成后发金币
    public int Pay { get; set; }            // 工资
    public int EndMinutes => StartMinutes + DurationMinutes;
}

/// <summary>
/// 每天凌晨 0 点(东八区)重新规划当天日程:
/// 从任务库 tasks.txt 随机挑 2~5 件(至少 1 件工作任务),放到随机时段(07:30~23:15,不与睡觉冲突),
/// 每件硬上限 90 分钟、之间至少空闲 45 分钟;同时生成 3~5 段“爱溜达时段”(走动概率大幅提高)。
/// 睡觉是固定作息 23:30~07:00,不算任务。
/// </summary>
public class DailyPlanner
{
    static readonly TimeZoneInfo Cst =
        TryTz("China Standard Time") ?? TryTz("Asia/Shanghai") ?? TimeZoneInfo.Utc;

    static TimeZoneInfo TryTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { return null; }
    }

    public static DateTime NowCst => TimeZoneInfo.ConvertTime(DateTime.UtcNow, Cst);

    public const int SleepStart = 23 * 60 + 30;   // 23:30
    public const int SleepEnd = 7 * 60;           // 07:00
    const int DayStart = SleepEnd + 30;           // 07:30
    const int DayEnd = SleepStart - 15;           // 23:15
    const int MinGap = 45;

    readonly string _path = Path.Combine(Store.Dir, "schedule.json");
    public List<ScheduledTask> Tasks { get; private set; } = new();
    public List<int[]> Walks { get; private set; } = new();   // [start,end] 分钟,爱溜达时段
    public string PlanDate { get; private set; } = "";
    public static string PoolPathUsed { get; private set; } = "(内置默认)";

    // ---------------- 任务库(tasks.txt) ----------------

    // name, min, max, isWork, intensity(工作强度 1~5,决定工资高低)
    static readonly (string name, int min, int max, bool work, int intensity)[] DefaultPool =
    {
        // 休闲
        ("练习剑术", 45, 90, false, 0), ("晨间散步", 30, 60, false, 0),
        ("读书写字", 40, 80, false, 0), ("打理花草", 30, 60, false, 0),
        ("烹饪点心", 40, 70, false, 0), ("午间小憩", 45, 90, false, 0),
        ("擦拭妖刀", 30, 50, false, 0), ("眺望远方发呆", 30, 60, false, 0),
        ("写俳句", 30, 45, false, 0), ("追番时间", 45, 90, false, 0),
        ("折纸手工", 30, 60, false, 0), ("打坐修炼", 30, 60, false, 0),
        ("整理房间", 30, 60, false, 0), ("画画涂鸦", 45, 90, false, 0),
        ("泡茶品茶", 30, 50, false, 0), ("研究点心配方", 40, 70, false, 0),
        // 工作(赚金币,第5列=工作强度 1~5)
        ("神社帮工", 60, 90, true, 3), ("帮邻居跑腿", 30, 60, true, 2),
        ("抄写符纸", 45, 80, true, 2), ("照看点心铺", 60, 90, true, 3),
        ("直播试胆大会", 45, 90, true, 4), ("除灵委托", 60, 90, true, 5),
        ("神乐舞表演", 40, 70, true, 4), ("守夜巡山", 60, 90, true, 5),
    };

    static string DefaultPoolText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 心海海的任务库:每天 0 点从这里随机挑 2~5 件(至少 1 件工作)、放到随机时段执行。");
        sb.AppendLine("# 休闲任务:名称,最短分钟,最长分钟              (时长限制在 15~90 分钟)");
        sb.AppendLine("# 工作任务:名称,最短分钟,最长分钟,工作,强度      (强度 1~5,越高工资越高)");
        sb.AppendLine("# 工资在完成时按【强度 × 时长】随机结算 75~200 金币,不用自己填。");
        sb.AppendLine("# 只写名称也行(默认 30~90 分钟)。# 开头是注释。改完保存,");
        sb.AppendLine("# 右键心海海 →「重新规划今天」立即生效;否则次日 0 点生效。");
        sb.AppendLine();
        foreach (var (n, a, b, w, it) in DefaultPool)
            sb.AppendLine(w ? $"{n},{a},{b},工作,{it}" : $"{n},{a},{b}");
        return sb.ToString();
    }

    static List<(string name, int min, int max, bool work, int intensity)> LoadPool()
    {
        string exeSide = Path.Combine(AppContext.BaseDirectory, "tasks.txt");
        string appData = Path.Combine(Store.Dir, "tasks.txt");
        try
        {
            if (!File.Exists(exeSide) && !File.Exists(appData))
                File.WriteAllText(appData, DefaultPoolText(), new UTF8Encoding(true));

            string path = File.Exists(exeSide) ? exeSide : appData;
            var list = new List<(string, int, int, bool, int)>();
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                var parts = line.Split(new[] { ',', ',', ';' });
                string name = parts[0].Trim();
                if (name.Length == 0) continue;

                int mn = 30, mx = 90;
                if (parts.Length >= 3 && int.TryParse(parts[1].Trim(), out int a) && int.TryParse(parts[2].Trim(), out int b))
                { mn = a; mx = b; }
                mn = Math.Clamp(mn, 15, 90);
                mx = Math.Clamp(mx, mn, 90);

                bool work = parts.Length >= 4 &&
                            (parts[3].Trim() == "工作" || parts[3].Trim().Equals("work", StringComparison.OrdinalIgnoreCase));
                int intensity = 3;
                if (work && parts.Length >= 5 && int.TryParse(parts[4].Trim(), out int it))
                    intensity = Math.Clamp(it, 1, 5);
                list.Add((name, mn, mx, work, intensity));
            }
            if (list.Count > 0) { PoolPathUsed = path; return list; }
        }
        catch { }
        PoolPathUsed = "(内置默认)";
        return DefaultPool.ToList();
    }

    /// <summary>按【工作强度 × 时长】随机结算工资:75~200 金币。</summary>
    static int ComputePay(int intensity, int dur, Random rnd)
    {
        double intNorm = (Math.Clamp(intensity, 1, 5) - 1) / 4.0;    // 0~1
        double durNorm = (Math.Clamp(dur, 15, 90) - 15) / 75.0;      // 0~1
        double factor = 0.55 * intNorm + 0.45 * durNorm;            // 强度权重略高
        double jitter = rnd.Next(-15, 16);
        return (int)Math.Clamp(Math.Round(75 + factor * 125 + jitter), 75, 200);
    }

    // ---------------- 每日规划 ----------------

    /// <summary>确保今天有规划;若刚生成新规划返回 true。</summary>
    public bool EnsureToday()
    {
        string today = NowCst.ToString("yyyy-MM-dd");
        if (PlanDate == today && Tasks.Count > 0) return false;

        try
        {
            if (File.Exists(_path))
            {
                var saved = JsonSerializer.Deserialize<SavedPlan>(File.ReadAllText(_path));
                if (saved != null && saved.date == today && saved.tasks is { Count: > 0 })
                {
                    Tasks = saved.tasks;
                    Walks = saved.walks ?? GenerateWalks(new Random());
                    PlanDate = today;
                    return false;
                }
            }
        }
        catch { }

        Plan(today);
        return true;
    }

    /// <summary>作废今天的计划并立刻重新随机规划。</summary>
    public bool Replan()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        PlanDate = "";
        Tasks = new List<ScheduledTask>();
        Walks = new List<int[]>();
        return EnsureToday();
    }

    void Plan(string today)
    {
        var rnd = new Random();
        var pool = LoadPool();

        // 2~5 件,至少 1 件工作任务
        int count = Math.Min(2 + rnd.Next(4), pool.Count);
        var works = pool.Where(p => p.work).ToList();
        if (works.Count == 0) works.Add(("神社帮工", 60, 90, true, 3));   // 任务库被改光了也兜底

        var picks = new List<(string name, int min, int max, bool work, int intensity)>
        {
            works[rnd.Next(works.Count)]                      // 先保证一件工作
        };
        picks.AddRange(pool.Where(p => p.name != picks[0].name)
                           .OrderBy(_ => rnd.Next())
                           .Take(Math.Max(1, count - 1)));    // 其余随机补足(至少凑到 2 件)

        var placed = new List<ScheduledTask>();
        foreach (var p in picks)
        {
            int dur = Math.Min(90, rnd.Next(p.min, p.max + 1));
            for (int attempt = 0; attempt < 400; attempt++)
            {
                int start = rnd.Next(DayStart, DayEnd - dur + 1);
                bool ok = placed.All(t => start >= t.EndMinutes + MinGap || start + dur <= t.StartMinutes - MinGap);
                if (ok)
                {
                    placed.Add(new ScheduledTask
                    {
                        Name = p.name, StartMinutes = start, DurationMinutes = dur,
                        IsWork = p.work, Pay = p.work ? ComputePay(p.intensity, dur, rnd) : 0
                    });
                    break;
                }
            }
        }

        Tasks = placed.OrderBy(t => t.StartMinutes).ToList();
        Walks = GenerateWalks(rnd);
        PlanDate = today;
        try { File.WriteAllText(_path, JsonSerializer.Serialize(new SavedPlan { date = today, tasks = Tasks, walks = Walks })); } catch { }
        Store.Log($"今日({today})规划完成(任务库:{PoolPathUsed},共{Tasks.Count}件):{Describe()};溜达时段:{WalksDescribe()}");
    }

    /// <summary>3~5 段“爱溜达时段”,每段 20~50 分钟,期间走动概率大幅提高。</summary>
    List<int[]> GenerateWalks(Random rnd)
    {
        var list = new List<int[]>();
        int n = 3 + rnd.Next(3);
        for (int i = 0; i < n; i++)
        {
            int len = 20 + rnd.Next(31);
            int start = rnd.Next(DayStart, DayEnd - len + 1);
            list.Add(new[] { start, start + len });
        }
        return list.OrderBy(w => w[0]).ToList();
    }

    public bool IsWalkTime(DateTime cst)
    {
        int m = cst.Hour * 60 + cst.Minute;
        return Walks.Any(w => m >= w[0] && m < w[1]);
    }

    public string Describe() =>
        Tasks.Count == 0 ? "今天没安排任务,全天有空~"
        : string.Join(",", Tasks.Select(t =>
            $"{t.Name} {Fmt(t.StartMinutes)}~{Fmt(t.EndMinutes)}" + (t.IsWork ? $"(工作+{t.Pay}金币)" : "")));

    public string WalksDescribe() =>
        Walks.Count == 0 ? "无" : string.Join("、", Walks.Select(w => $"{Fmt(w[0])}~{Fmt(w[1])}"));

    public static string Fmt(int m) { m %= 24 * 60; return $"{m / 60:D2}:{m % 60:D2}"; }

    /// <summary>当前正在进行的规划任务(不含睡觉);无则返回 null。</summary>
    public ScheduledTask CurrentTask()
    {
        var now = NowCst;
        int nowMin = now.Hour * 60 + now.Minute;
        return Tasks.FirstOrDefault(t => nowMin >= t.StartMinutes && nowMin < t.EndMinutes);
    }

    public static bool IsSleepTime()
    {
        var now = NowCst;
        int m = now.Hour * 60 + now.Minute;
        return m >= SleepStart || m < SleepEnd;
    }

    public static int MinutesUntilSleepEnd()
    {
        var now = NowCst;
        int m = now.Hour * 60 + now.Minute;
        return (SleepEnd - m + 24 * 60) % (24 * 60);
    }

    class SavedPlan
    {
        public string date { get; set; }
        public List<ScheduledTask> tasks { get; set; }
        public List<int[]> walks { get; set; }
    }
}
