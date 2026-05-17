using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BG3LocTool.Core;

namespace BG3LocTool.Translators;

public sealed class OpenAIOptions
{
    public required string BaseUrl { get; init; }
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public string? SystemPromptOverride { get; init; }
    public int MaxConcurrency { get; init; } = 4;
    public int TimeoutSeconds { get; init; } = 60;
    public double Temperature { get; init; } = 0.3;
}

public sealed class OpenAICompatibleTranslator : ITranslator
{
    private const string DefaultSystemPrompt =
        "You are a precise game-mod localization translator. Translate the user's source text into {0}. " +
        "CRITICAL RULES: (1) Output ONLY the translation, no quotes, no commentary. " +
        "(2) Preserve EVERY placeholder verbatim, including but not limited to: square-bracket numbers like [1], " +
        "curly-brace tokens like {{0}}, XML/LSTag elements like <LSTag Type='Hotkey' Tooltip='ChatLog'/>, " +
        "ampersand entities like &lt;. (3) Preserve leading/trailing whitespace and newlines. " +
        "(4) Keep proper nouns (character names, place names) consistent with the provided context if any. " +
        "(5) If the source is empty or untranslatable (pure placeholder), output it unchanged.";

    private readonly OpenAIOptions _opts;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public string Name => $"openai:{_opts.Model}";

    public OpenAICompatibleTranslator(OpenAIOptions opts, HttpClient? http = null)
    {
        _opts = opts;
        _http = http ?? new HttpClient();
        _ownsClient = http is null;
    }

    public async Task<IReadOnlyList<TranslateResult>> TranslateBatchAsync(
        IReadOnlyList<string> sources,
        string targetLang,
        IReadOnlyList<string>? contextSamples,
        CancellationToken ct)
    {
        var results = new TranslateResult[sources.Count];
        using var gate = new SemaphoreSlim(_opts.MaxConcurrency, _opts.MaxConcurrency);

        var basePrompt = string.Format(_opts.SystemPromptOverride ?? DefaultSystemPrompt, targetLang);
        // System prompt 三层（同批次内固定，OpenAI / Anthropic 自动 prompt cache 摊销成本）：
        //   1. basePrompt:           翻译规范（短）
        //   2. Bg3Glossary:          50+ D&D/BG3 高频术语字典（短）
        //   3. Bg3ModdingWiki:       6KB 模 modding 语境知识（占位符规范、文体、状态枚举、Patch 8 现状）
        // 每条 source 之后再 append per-source BuildConstraintBlock（按英文文本动态命中的术语强约束）
        var generalSystem = basePrompt
            + "\n\n" + Bg3Glossary.ForSystemPrompt
            + "\n\n" + Bg3ModdingWiki.ForSystemPrompt;
        var contextBlock = BuildContextBlock(contextSamples);
        var glossary = Bg3OfficialGlossary.Default;

        var tasks = new Task[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            int idx = i;
            tasks[i] = Task.Run(async () =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var src = sources[idx];
                    // Per-source: append BG3 vanilla term constraints (lock proper-noun/mechanic translations,
                    // but the LLM still owns sentence grammar and natural Chinese flow)
                    var officialTerms = glossary?.BuildConstraintBlock(src) ?? "";
                    var system = string.IsNullOrEmpty(officialTerms) ? generalSystem : generalSystem + "\n\n" + officialTerms;
                    results[idx] = await TranslateOneAsync(src, system, contextBlock, ct).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }, ct);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var retryTasks = new List<Task>();
        for (int i = 0; i < sources.Count; i++)
        {
            int idx = i;
            var r = results[idx];
            if (r.Translation is null) continue;
            var placeholderOk = PlaceholderValidator.IsValid(sources[idx], r.Translation);
            var termsOk = glossary is null || glossary.VerifyTerms(sources[idx], r.Translation).Count == 0;
            if (placeholderOk && termsOk) continue;
            retryTasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var required = PlaceholderValidator.ExtractPlaceholders(sources[idx]);
                    var missingTerms = glossary?.VerifyTerms(sources[idx], r.Translation!) ?? new List<TermHit>();
                    var retry = await TranslateOneStrictAsync(sources[idx], targetLang, required, missingTerms, ct).ConfigureAwait(false);
                    if (retry.Translation != null
                        && PlaceholderValidator.IsValid(sources[idx], retry.Translation)
                        && (glossary is null || glossary.VerifyTerms(sources[idx], retry.Translation).Count == 0))
                        results[idx] = retry;
                }
                finally
                {
                    gate.Release();
                }
            }, ct));
        }
        if (retryTasks.Count > 0) await Task.WhenAll(retryTasks).ConfigureAwait(false);
        return results;
    }

    private async Task<TranslateResult> TranslateOneStrictAsync(
        string source, string targetLang,
        IReadOnlyList<string> requiredPlaceholders,
        IReadOnlyList<TermHit> missingTerms,
        CancellationToken ct)
    {
        var phList = requiredPlaceholders.Count == 0 ? "(none)" : string.Join(" , ", requiredPlaceholders);
        var termList = missingTerms.Count == 0 ? "" :
            "\nALSO MISSING THESE OFFICIAL BG3 TERMS — output MUST include each Chinese term verbatim:\n" +
            string.Join("\n", missingTerms.Select(t => $"  · {t.En}  →  {t.Zh}"));
        var strict =
            $"You are a precise game-mod localization translator. Translate the user's source text into {targetLang}. " +
            "PREVIOUS ATTEMPT FAILED VALIDATION. " +
            $"The output MUST contain these EXACT placeholder tokens (in any order): {phList}. " +
            termList +
            "\nTranslate the source preserving these tokens and official term verbatim. " +
            "Only output the translation, no commentary, no quotes." +
            "\n\n" + Bg3Glossary.ForSystemPrompt;
        return await TranslateOneAsync(source, strict, null, ct).ConfigureAwait(false);
    }

    private static string? BuildContextBlock(IReadOnlyList<string>? contextSamples)
    {
        if (contextSamples is null || contextSamples.Count == 0) return null;
        var sb = new StringBuilder("Example terminology already used in this mod:\n");
        int n = Math.Min(3, contextSamples.Count);
        for (int i = 0; i < n; i++)
            sb.Append("  ").Append(contextSamples[i]).Append('\n');
        return sb.ToString();
    }

    private async Task<TranslateResult> TranslateOneAsync(string source, string systemPrompt, string? contextBlock, CancellationToken ct)
    {
        // BaseUrl can be either a base (e.g. "https://api.deepseek.com/v1") or a full endpoint
        // (e.g. "https://.../serving-endpoints/<model>/invocations" — Databricks). Detect & don't double-suffix.
        var baseTrim = _opts.BaseUrl.TrimEnd('/');
        var url = (baseTrim.EndsWith("/invocations", StringComparison.OrdinalIgnoreCase)
                || baseTrim.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            ? baseTrim
            : baseTrim + "/chat/completions";
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        if (contextBlock is not null)
        {
            messages.Add(new { role = "user", content = contextBlock });
            messages.Add(new { role = "assistant", content = "Understood. I will keep terminology consistent." });
        }
        messages.Add(new { role = "user", content = source });

        // Databricks-served Anthropic models reject `temperature` (HTTP 400 BAD_REQUEST).
        // Detect by URL containing "/invocations" (Databricks endpoint pattern) and omit.
        bool omitTemperature = url.Contains("/invocations", StringComparison.OrdinalIgnoreCase);
        object payload = omitTemperature
            ? new { model = _opts.Model, messages, max_tokens = 2000, stream = false }
            : new { model = _opts.Model, messages, max_tokens = 2000, temperature = _opts.Temperature, stream = false };
        var body = JsonSerializer.Serialize(payload);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_opts.TimeoutSeconds));

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);

            using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
            var raw = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new TranslateResult(source, null, $"HTTP {(int)resp.StatusCode}: {Truncate(raw, 200)}");

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return new TranslateResult(source, null, "missing choices");
            if (!choices[0].TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content))
                return new TranslateResult(source, null, "missing message.content");
            var text = content.GetString()?.Trim();
            if (string.IsNullOrEmpty(text))
                return new TranslateResult(source, null, "empty content");
            return new TranslateResult(source, text, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new TranslateResult(source, null, "timeout");
        }
        catch (Exception ex)
        {
            return new TranslateResult(source, null, ex.Message);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
