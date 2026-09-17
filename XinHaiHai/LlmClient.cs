using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XinHaiHai;

/// <summary>
/// 大模型对话客户端:OpenAI 兼容接口(/v1/chat/completions)。
/// 通过可修改的人设(persona)生成互动台词;超时(默认 6 秒)或任何失败都返回 null,
/// 调用方据此“取消本次对话台词”,不影响其他功能。
/// </summary>
public static class LlmClient
{
    // 复用单个 HttpClient(避免 socket 耗尽);超时在每次请求用 CTS 单独控制。
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>当前是否处于大模型对话模式且配置完整。</summary>
    public static bool Enabled =>
        Store.Config.dialogueMode == "llm"
        && !string.IsNullOrWhiteSpace(Store.Config.llmApiKey)
        && !string.IsNullOrWhiteSpace(Store.Config.llmBaseUrl)
        && !string.IsNullOrWhiteSpace(Store.Config.llmModel);

    /// <summary>
    /// 让大模型以当前人设生成一句互动台词。
    /// scene 描述这次互动(如“主人摸了摸头”),situation 给出可选的状态上下文。
    /// 返回 null 表示超时或失败——调用方应静默跳过本次台词。
    /// </summary>
    public static async Task<string> ChatAsync(string scene, string situation = null)
    {
        if (!Enabled) return null;

        var cfg = Store.Config;
        int timeoutMs = Math.Max(1, cfg.llmTimeoutSeconds) * 1000;
        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            string url = cfg.llmBaseUrl.TrimEnd('/') + "/chat/completions";

            string persona = string.IsNullOrWhiteSpace(cfg.llmPersona)
                ? "你是一只桌面宠物,性格活泼可爱,称呼使用者为“主人”。"
                : cfg.llmPersona;

            // 系统提示:固定人设 + 硬性约束(只说中文、简短、单句、不加旁白)
            string sys =
                persona.Replace("{name}", cfg.petName) + "\n\n" +
                "【输出要求】\n" +
                "1. 只用简体中文回答,禁止出现任何其他语言、拼音或翻译。\n" +
                "2. 只输出桌宠要说的这一句话本身,不要加引号、旁白、括号说明、表情描述或前后缀。\n" +
                "3. 一句话即可,尽量短(不超过 40 字),符合人设语气。\n" +
                $"4. 你的名字叫「{cfg.petName}」。";

            string user = "现在发生的互动:" + scene;
            if (!string.IsNullOrWhiteSpace(situation)) user += "\n当前状态:" + situation;
            user += "\n请只用一句中文台词回应。";

            var payload = new
            {
                model = cfg.llmModel,
                messages = new object[]
                {
                    new { role = "system", content = sys },
                    new { role = "user", content = user }
                },
                temperature = cfg.llmTemperature,
                max_tokens = 120,
                stream = false
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + cfg.llmApiKey);

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token);
            string body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                Store.Log($"大模型接口返回 {(int)resp.StatusCode}:{Trim(body)}");
                return null;
            }

            string text = ExtractContent(body);
            if (string.IsNullOrWhiteSpace(text)) { Store.Log("大模型返回内容为空"); return null; }
            return Clean(text);
        }
        catch (OperationCanceledException)
        {
            Store.Log($"大模型对话超时({cfg.llmTimeoutSeconds}s),已取消本次台词");
            return null;
        }
        catch (Exception ex)
        {
            Store.Log("大模型对话失败:" + ex.Message);
            return null;
        }
    }

    /// <summary>从 OpenAI 兼容响应里取 choices[0].message.content。</summary>
    static string ExtractContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;
            var msg = choices[0].GetProperty("message");
            return msg.GetProperty("content").GetString();
        }
        catch { return null; }
    }

    /// <summary>清洗模型输出:去引号/首尾空白/多余换行,压成单句。</summary>
    static string Clean(string s)
    {
        s = s.Trim();
        // 去掉包裹的成对引号
        if (s.Length >= 2)
        {
            char a = s[0], b = s[^1];
            if ((a == '"' && b == '"') || (a == '「' && b == '」') ||
                (a == '“' && b == '”') || (a == '\'' && b == '\''))
                s = s[1..^1].Trim();
        }
        // 合并多余换行/空格
        var parts = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        s = string.Join(" ", parts).Trim();
        // 太长截断,避免气泡爆框
        if (s.Length > 60) s = s[..60] + "…";
        return s;
    }

    static string Trim(string s) => s == null ? "" : (s.Length > 200 ? s[..200] : s);

    /// <summary>连通性自测:发一句问候,返回结果或错误说明(供设置窗“测试”按钮用)。</summary>
    public static async Task<(bool ok, string text)> TestAsync()
    {
        if (!Enabled) return (false, "配置不完整(需填写接口地址、密钥、模型,并把对话模式设为大模型)。");
        string r = await ChatAsync("主人第一次连接大模型,跟她打个招呼");
        return r == null ? (false, "没有拿到回复(超时或接口错误,详见 log.txt)。") : (true, r);
    }
}
