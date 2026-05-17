using LSLib.LS;
using LSLib.LS.Enums;

namespace BG3LocTool.Core;

public static class PakHandler
{
    public static void Extract(string pakPath, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        new Packager().UncompressPackage(pakPath, outputDir, null);
    }

    public static async Task BuildAsync(string contentDir, string outputPakPath)
    {
        var build = new PackageBuildData
        {
            Version = PackageVersion.V18,
            Compression = CompressionMethod.LZ4,
            CompressionLevel = LSCompressionLevel.Default,
            // Skip sidecar files (e.g. _translation_status.json, _bg3mm_group.txt) — they're tool-internal, not BG3 mod data.
            Files = Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories)
                .Where(fs => !Path.GetFileName(fs).StartsWith('_'))
                .Select(fs => PackageBuildInputFile.CreateFromFilesystem(fs, Path.GetRelativePath(contentDir, fs).Replace('\\', '/')))
                .ToList()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPakPath))!);
        await new Packager().CreatePackage(outputPakPath, contentDir, build);
    }
}
