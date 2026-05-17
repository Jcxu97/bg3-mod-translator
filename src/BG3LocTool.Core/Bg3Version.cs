namespace BG3LocTool.Core;

public readonly record struct Bg3Version(byte Major, byte Minor, ushort Revision, uint Build)
{
    public long Encode() =>
        ((long)Major << 55) | ((long)Minor << 47) | ((long)Revision << 31) | (long)(Build & 0x7FFFFFFF);

    public static Bg3Version Decode(long v) => new(
        Major:    (byte)((v >> 55) & 0xFF),
        Minor:    (byte)((v >> 47) & 0xFF),
        Revision: (ushort)((v >> 31) & 0xFFFF),
        Build:    (uint)(v & 0x7FFFFFFF));

    public override string ToString() => $"{Major}.{Minor}.{Revision}.{Build}";

    public static Bg3Version Parse(string s)
    {
        var p = s.Split('.');
        if (p.Length != 4) throw new FormatException($"expected M.m.r.b, got '{s}'");
        return new(byte.Parse(p[0]), byte.Parse(p[1]), ushort.Parse(p[2]), uint.Parse(p[3]));
    }
}
