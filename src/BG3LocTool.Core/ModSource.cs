using System.IO.Compression;

namespace BG3LocTool.Core;

public sealed class ModSource : IDisposable
{
    public string ContentDir { get; }
    private readonly string? _ownedTempDir;

    private ModSource(string contentDir, string? ownedTempDir)
    {
        ContentDir = contentDir;
        _ownedTempDir = ownedTempDir;
    }

    public static ModSource Open(string path)
    {
        if (Directory.Exists(path)) return new ModSource(path, null);
        if (!File.Exists(path)) throw new FileNotFoundException(path);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var temp = Path.Combine(Path.GetTempPath(), "BG3LocTool_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        if (ext == ".pak")
        {
            PakHandler.Extract(path, temp);
            return new ModSource(temp, temp);
        }
        if (ext == ".zip")
        {
            var unzipDir = Path.Combine(temp, "_zip");
            ZipFile.ExtractToDirectory(path, unzipDir);
            var inner = Directory.EnumerateFiles(unzipDir, "*.pak", SearchOption.AllDirectories).FirstOrDefault()
                        ?? throw new InvalidDataException("zip does not contain a .pak");
            var extractDir = Path.Combine(temp, "content");
            PakHandler.Extract(inner, extractDir);
            return new ModSource(extractDir, temp);
        }
        throw new NotSupportedException($"unsupported input: {ext}");
    }

    public string FindModFolder()
    {
        var modsDir = Path.Combine(ContentDir, "Mods");
        if (!Directory.Exists(modsDir)) throw new InvalidDataException($"no Mods/ directory in {ContentDir}");
        var dirs = Directory.GetDirectories(modsDir);
        if (dirs.Length != 1) throw new InvalidDataException($"expected exactly 1 mod folder in Mods/, found {dirs.Length}");
        return dirs[0];
    }

    public void Dispose()
    {
        if (_ownedTempDir != null && Directory.Exists(_ownedTempDir))
            try { Directory.Delete(_ownedTempDir, true); } catch { }
    }
}
