using System.Net.Http;
using System.Text.Json;
using BG3LocTool.Core;

namespace BG3LocTool.Translators;

public sealed class GoogleFreeTranslator : ITranslator
{
    private const string EndpointFormat =
        "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={0}&dt=t&q={1}";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public string Name => "google-free";

    public GoogleFreeTranslator(HttpClient? http = null)
    {
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
        using var gate = new SemaphoreSlim(4, 4);

        var tasks = new Task[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            int idx = i;
            tasks[i] = Task.Run(async () =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    results[idx] = await TranslateOneAsync(sources[idx], targetLang, ct).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }, ct);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<TranslateResult> TranslateOneAsync(string source, string targetLang, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(source))
            return new TranslateResult(source, source, null);

        var url = string.Format(EndpointFormat, Uri.EscapeDataString(targetLang), Uri.EscapeDataString(source));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            using var resp = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new TranslateResult(source, null, $"HTTP {(int)resp.StatusCode}");

            var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var translated = ParseTranslation(json);
            if (translated is null)
                return new TranslateResult(source, null, "parse: empty translation");
            return new TranslateResult(source, translated, null);
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

    private static string? ParseTranslation(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
        var segments = root[0];
        if (segments.ValueKind != JsonValueKind.Array) return null;

        var sb = new System.Text.StringBuilder();
        foreach (var seg in segments.EnumerateArray())
        {
            if (seg.ValueKind != JsonValueKind.Array || seg.GetArrayLength() == 0) continue;
            var first = seg[0];
            if (first.ValueKind == JsonValueKind.String)
                sb.Append(first.GetString());
        }
        var s = sb.ToString();
        return s.Length == 0 ? null : s;
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
