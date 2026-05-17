using LSLib.LS;
using LSLib.LS.Enums;

namespace BG3LocTool.Core;

public sealed record MetaModuleInfo(string Author, string Name, string Folder, string Description, string Uuid, long Version64);

public static class MetaEditor
{
    public static MetaModuleInfo ReadModuleInfo(string lsxPath)
    {
        using var fs = File.OpenRead(lsxPath);
        using var reader = new LSXReader(fs);
        var res = reader.Read();
        var node = ModuleInfoNode(res);
        string Get(string id) => (node.Attributes.TryGetValue(id, out var a) ? a.Value?.ToString() : null) ?? "";
        long ver = node.Attributes.TryGetValue("Version64", out var v) && v.Value is long l ? l : 0L;
        return new(Get("Author"), Get("Name"), Get("Folder"), Get("Description"), Get("UUID"), ver);
    }

    public static void RewriteForChs(string srcLsxPath, string dstLsxPath, MetaModuleInfo overrides)
    {
        using var fs = File.OpenRead(srcLsxPath);
        Resource res;
        using (var reader = new LSXReader(fs)) res = reader.Read();
        var info = ModuleInfoNode(res);
        SetAttr(info, "Author", AttributeType.LSString, overrides.Author);
        SetAttr(info, "Name", AttributeType.LSString, overrides.Name);
        SetAttr(info, "Folder", AttributeType.LSString, overrides.Folder);
        SetAttr(info, "Description", AttributeType.LSString, overrides.Description);
        SetAttr(info, "UUID", AttributeType.FixedString, overrides.Uuid);
        SetAttr(info, "Version64", AttributeType.Int64, overrides.Version64);
        if (info.Children.TryGetValue("PublishVersion", out var pvList) && pvList.Count > 0)
            SetAttr(pvList[0], "Version64", AttributeType.Int64, overrides.Version64);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dstLsxPath))!);
        using var outFs = File.Create(dstLsxPath);
        var writer = new LSXWriter(outFs) { PrettyPrint = true, Version = LSXVersion.V4 };
        writer.Write(res);
    }

    private static Node ModuleInfoNode(Resource res) =>
        res.Regions["Config"].Children["ModuleInfo"][0];

    private static void SetAttr(Node n, string id, AttributeType type, object value)
    {
        if (!n.Attributes.TryGetValue(id, out var a))
        {
            a = new NodeAttribute(type);
            n.Attributes[id] = a;
        }
        a.Value = value;
    }
}
