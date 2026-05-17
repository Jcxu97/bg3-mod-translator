using LSLib.LS;

namespace BG3LocTool.Core;

public sealed record LocaEntry(string Handle, ushort Version, string Text);

public static class LocaIO
{
    public static List<LocaEntry> Load(string path)
    {
        var res = LocaUtils.Load(path);
        return res.Entries.Select(e => new LocaEntry(e.Key, e.Version, e.Text ?? "")).ToList();
    }

    public static void SaveXml(IEnumerable<LocaEntry> entries, string path)
    {
        var res = new LocaResource { Entries = entries.Select(e => new LocalizedText { Key = e.Handle, Version = e.Version, Text = e.Text }).ToList() };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        LocaUtils.Save(res, path, LocaFormat.Xml);
    }
}
