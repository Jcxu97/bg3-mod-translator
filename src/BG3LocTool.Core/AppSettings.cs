using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BG3LocTool.Core;

public sealed class AppSettings
{
    public string Mode { get; set; } = "FromScratch";  // "FromScratch" | "Upgrade"
    public string SelectedPreset { get; set; } = "DeepSeek";
    public string BaseUrl { get; set; } = "";

    [JsonIgnore] public string ApiKey { get; set; } = "";

    // Stored in JSON as DPAPI-encrypted base64 (CurrentUser scope) so a user-level settings.json
    // leak doesn't immediately expose the key. Backwards-compatible: legacy plaintext values still load.
    [JsonPropertyName("ApiKey")]
    public string ApiKeyEncrypted
    {
        get => string.IsNullOrEmpty(ApiKey) ? "" : "enc:" + EncryptDpapi(ApiKey);
        set => ApiKey = value.StartsWith("enc:", StringComparison.Ordinal) ? DecryptDpapi(value[4..]) : value;
    }

    public string Model { get; set; } = "";
    public string Author { get; set; } = "";
    public string VersionText { get; set; } = "1.0.0.1";
    public string OutputDir { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BG3CHS");

    private static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BG3LocTool", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string EncryptDpapi(string plain)
    {
        if (!OperatingSystem.IsWindows()) return plain;
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return plain;  // DPAPI failure: degrade to plaintext rather than lose the value
        }
    }

    private static string DecryptDpapi(string b64)
    {
        if (!OperatingSystem.IsWindows()) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "";
        }
    }
}
