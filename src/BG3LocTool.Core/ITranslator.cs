namespace BG3LocTool.Core;

public sealed record TranslateResult(string Source, string? Translation, string? Error);

public interface ITranslator
{
    string Name { get; }

    Task<IReadOnlyList<TranslateResult>> TranslateBatchAsync(
        IReadOnlyList<string> sources,
        string targetLang,
        IReadOnlyList<string>? contextSamples,
        CancellationToken ct);
}
