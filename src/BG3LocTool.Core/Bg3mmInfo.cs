using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace BG3LocTool.Core;

public static class Bg3mmInfo
{
    public static string? TryReadGroupUuid(string? bg3mmZipPath)
    {
        if (string.IsNullOrWhiteSpace(bg3mmZipPath) || !File.Exists(bg3mmZipPath)) return null;
        if (!bg3mmZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var zip = ZipFile.OpenRead(bg3mmZipPath);
            var entry = zip.GetEntry("info.json");
            if (entry == null) return null;
            using var s = entry.Open();
            using var r = new StreamReader(s);
            using var doc = JsonDocument.Parse(r.ReadToEnd());
            return doc.RootElement.GetProperty("Mods")[0].GetProperty("Group").GetString();
        }
        catch { return null; }
    }

    public static void WriteZip(string outputZipPath, string pakPath, MetaModuleInfo info, string? groupUuid = null)
    {
        var pakBytes = File.ReadAllBytes(pakPath);
        var md5 = Convert.ToHexString(MD5.HashData(pakBytes)).ToLowerInvariant();
        var manifest = new
        {
            Mods = new[]
            {
                new
                {
                    Author = info.Author,
                    Name = info.Name,
                    Folder = info.Folder,
                    Version = info.Version64.ToString(),
                    Description = info.Description,
                    UUID = info.Uuid,
                    Created = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz"),
                    Dependencies = Array.Empty<object>(),
                    Group = groupUuid ?? Guid.NewGuid().ToString()
                }
            },
            MD5 = md5
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = false });

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputZipPath))!);
        if (File.Exists(outputZipPath)) File.Delete(outputZipPath);
        using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry("info.json").Open())) w.Write(json);
        var pakEntry = zip.CreateEntry(Path.GetFileName(pakPath));
        using var pakOut = pakEntry.Open();
        pakOut.Write(pakBytes);
    }
}
