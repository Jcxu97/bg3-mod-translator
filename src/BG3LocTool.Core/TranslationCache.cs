using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BG3LocTool.Core;

public sealed record CacheStats(string ModUuid, int Count);

public sealed class TranslationCache : IDisposable
{
    private readonly SqliteConnection _conn;

    public TranslationCache(string? dbPath = null)
    {
        var path = dbPath ?? DefaultPath();
        if (path != ":memory:")
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        _conn = new SqliteConnection($"Data Source={path}");
        _conn.Open();

        // schema v2: per-mod scoping. Empty mod_uuid means "global" (rare; we always pass real UUID now).
        // If migrating from v1 (no mod_uuid column), drop the table — translations are fast to rebuild and
        // mixed-mod entries from v1 may be inaccurate, so a clean reset is safer than a complex migration.
        EnsureSchemaV2();
    }

    private void EnsureSchemaV2()
    {
        using var check = _conn.CreateCommand();
        check.CommandText = "PRAGMA table_info(translations)";
        bool tableExists = false, hasModUuid = false;
        using (var r = check.ExecuteReader())
            while (r.Read()) { tableExists = true; if ((string)r["name"] == "mod_uuid") hasModUuid = true; }
        if (tableExists && !hasModUuid)
        {
            using var drop = _conn.CreateCommand();
            drop.CommandText = "DROP TABLE translations";
            drop.ExecuteNonQuery();
        }
        using var create = _conn.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS translations(
              source_hash TEXT NOT NULL,
              target_lang TEXT NOT NULL,
              mod_uuid TEXT NOT NULL DEFAULT '',
              source TEXT NOT NULL,
              target TEXT NOT NULL,
              translator TEXT NOT NULL,
              created_at INTEGER NOT NULL,
              PRIMARY KEY (source_hash, target_lang, mod_uuid)
            );
            """;
        create.ExecuteNonQuery();
    }

    public string? Get(string source, string targetLang, string modUuid = "")
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT target FROM translations WHERE source_hash = $h AND target_lang = $l AND mod_uuid = $m";
        cmd.Parameters.AddWithValue("$h", Hash(source));
        cmd.Parameters.AddWithValue("$l", targetLang);
        cmd.Parameters.AddWithValue("$m", modUuid);
        return cmd.ExecuteScalar() as string;
    }

    public void Put(string source, string target, string targetLang, string translatorName, string modUuid = "")
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO translations(source_hash, target_lang, mod_uuid, source, target, translator, created_at)
            VALUES($h, $l, $m, $s, $t, $tr, $c)
            ON CONFLICT(source_hash, target_lang, mod_uuid) DO UPDATE SET
              target = excluded.target,
              translator = excluded.translator,
              created_at = excluded.created_at;
            """;
        cmd.Parameters.AddWithValue("$h", Hash(source));
        cmd.Parameters.AddWithValue("$l", targetLang);
        cmd.Parameters.AddWithValue("$m", modUuid);
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$t", target);
        cmd.Parameters.AddWithValue("$tr", translatorName);
        cmd.Parameters.AddWithValue("$c", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
    }

    public List<CacheStats> Stats()
    {
        var list = new List<CacheStats>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT mod_uuid, COUNT(*) AS n FROM translations GROUP BY mod_uuid ORDER BY n DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new(r.GetString(0), r.GetInt32(1)));
        return list;
    }

    public int Clear(string? modUuid = null)
    {
        using var cmd = _conn.CreateCommand();
        if (modUuid is null) { cmd.CommandText = "DELETE FROM translations"; }
        else { cmd.CommandText = "DELETE FROM translations WHERE mod_uuid = $m"; cmd.Parameters.AddWithValue("$m", modUuid); }
        return cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();

    private static string Hash(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "BG3LocTool", "cache.db");
}
