using System.IO;
using System.Reflection;
using System.Text.Json;

namespace XinHaiHai;

/// <summary>一份台词包(data\lines_*.json 的结构)。</summary>
public class DialoguePack
{
    public string style { get; set; }
    public Dictionary<string, List<string>> lines { get; set; } = new();
    public Dictionary<string, List<string>> foods { get; set; } = new();
    public Dictionary<string, List<string>> drinks { get; set; } = new();
    public Dictionary<string, List<string>> gifts { get; set; } = new();
}

/// <summary>
/// 台词库:按外观模式(official/custom/live2d)加载对应 JSON,互动时随机抽一条。
/// 文件在 exe 旁的 data\lines_{mode}.json,可自由编辑;缺失时自动从内置资源释放默认版。
/// official/custom=丛雨风,live2d=初音未来风。
/// </summary>
public static class DialogueDB
{
    static readonly Random R = new();
    static DialoguePack pack = new();
    public static string CurrentPersona { get; private set; } = "";

    /// <summary>切换台词人格并重新读盘(official / custom / live2d)。</summary>
    public static void SetPersona(string skinMode)
    {
        string key = skinMode switch { "custom" => "custom", "live2d" => "live2d", _ => "official" };
        CurrentPersona = key;
        var p = LoadPack(key);
        if (p != null)
        {
            pack = p;
            int items = p.foods.Count + p.drinks.Count + p.gifts.Count;
            Store.Log($"台词库已加载:lines_{key}.json(风格 {p.style},类别 {p.lines.Count},物品 {items})");
        }
        else
        {
            pack = new DialoguePack();
            Store.Log($"台词库 lines_{key}.json 不可用,回退到内置台词");
        }
    }

    static DialoguePack LoadPack(string key)
    {
        string file = $"lines_{key}.json";
        string dir = Path.Combine(AppContext.BaseDirectory, "data");
        string path = Path.Combine(dir, file);
        try
        {
            if (!File.Exists(path)) ExtractDefault(dir, file);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<DialoguePack>(File.ReadAllText(path));
        }
        catch (Exception ex) { Store.Log($"台词库 {file} 解析失败:{ex.Message}"); }
        return null;
    }

    /// <summary>把嵌入在 exe 里的默认台词包释放到 data 文件夹(便于用户编辑)。</summary>
    static void ExtractDefault(string dir, string file)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var asm = Assembly.GetExecutingAssembly();
            string res = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
            if (res == null) return;
            using var s = asm.GetManifestResourceStream(res);
            using var f = File.Create(Path.Combine(dir, file));
            s.CopyTo(f);
            Store.Log($"已释放默认台词包 data\\{file}");
        }
        catch (Exception ex) { Store.Log($"释放台词包 {file} 失败:{ex.Message}"); }
    }

    /// <summary>取某类别随机一条并替换占位符;JSON 缺失该类别时返回 null(调用方用内置台词兜底)。</summary>
    public static string Pick(string category, params (string k, string v)[] vars)
    {
        if (pack.lines == null || !pack.lines.TryGetValue(category, out var list) || list == null || list.Count == 0)
            return null;
        return Fill(list[R.Next(list.Count)], vars);
    }

    /// <summary>取物品台词:section 为 foods / drinks / gifts,key 为物品 Key。</summary>
    public static string PickItem(string section, string key)
    {
        var dict = section switch
        {
            "foods" => pack.foods,
            "drinks" => pack.drinks,
            "gifts" => pack.gifts,
            _ => null
        };
        if (dict == null || !dict.TryGetValue(key, out var list) || list == null || list.Count == 0)
            return null;
        return Fill(list[R.Next(list.Count)], Array.Empty<(string, string)>());
    }

    static string Fill(string s, (string k, string v)[] vars)
    {
        s = s.Replace("{Name}", Store.Config.petName);
        foreach (var (k, v) in vars) s = s.Replace("{" + k + "}", v ?? "");
        return s;
    }
}
