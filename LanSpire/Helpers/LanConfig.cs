using System.IO;
using System.Text.Json.Nodes;

namespace LanSpire.Helpers;

/// <summary>
/// File-based config reader. Replaces AndroidSettingsBridge for PC.
/// Reads from lan_config.json next to the mod DLL.
/// </summary>
public static class LanConfig
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static DateTime _lastReadUtc;
    private static JsonDocument? _cached;

    private static string ConfigPath
    {
        get
        {
            try
            {
                var userDir = ProjectSettings.GlobalizePath("user://");
                if (string.IsNullOrEmpty(userDir))
                    userDir = Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                        "SlayTheSpire2");
                var modDir = Path.Combine(userDir, "LanSpire");
                Directory.CreateDirectory(modDir);
                return Path.Combine(modDir, "lan_config.json");
            }
            catch
            {
                var asmDir = Path.GetDirectoryName(typeof(LanConfig).Assembly.Location);
                return string.IsNullOrEmpty(asmDir) ? "lan_config.json" : Path.Combine(asmDir, "lan_config.json");
            }
        }
    }

    /// <summary>
    /// One-time migration: if the user has an existing config in the old
    /// "STS2LanPc" folder (from a previous version of this mod), copy it
    /// to the new "LanSpire" folder. Only runs once - if the new config
    /// already exists, the old folder is left alone.
    /// </summary>
    public static void TryMigrateFromOldFolder()
    {
        try
        {
            var userDir = ProjectSettings.GlobalizePath("user://");
            if (string.IsNullOrEmpty(userDir))
                userDir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "SlayTheSpire2");

            var oldConfigPath = Path.Combine(userDir, "STS2LanPc", "lan_config.json");
            var newConfigPath = Path.Combine(userDir, "LanSpire", "lan_config.json");

            if (!File.Exists(oldConfigPath))
                return;
            if (File.Exists(newConfigPath))
            {
                PatchHelper.Log($"Config migration skipped - new config already exists at {newConfigPath}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(newConfigPath)!);
            File.Copy(oldConfigPath, newConfigPath);
            PatchHelper.Log($"Config migrated from {oldConfigPath} to {newConfigPath}");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Config migration failed (non-fatal): {ex}");
        }
    }

    /// <summary>
    /// Creates a default config file if none exists yet (first run / fresh
    /// install). Merges with existing config - only adds missing keys.
    /// </summary>
    public static void EnsureDefaults()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return;
            var defaults = new System.Text.Json.Nodes.JsonObject
            {
                ["_comment"] = "LanSpire LAN Multiplayer Mod config. Edit this file to change settings.",
                ["lan_multiplayer_enabled"] = true,
                ["lan_use_custom_player_id"] = false,
                ["lan_custom_player_id"] = "",
                ["lan_join_host"] = "",
                ["lan_join_port"] = 33771,
                ["lan_compatibility_mod_names"] = new System.Text.Json.Nodes.JsonArray(),
                ["max_multiplayer_players"] = 4,
                ["max_multiplayer_enabled"] = true,
                ["lan_player_id"] = "",
                ["lan_multiplayer_save_player_id"] = "",
                ["resilience_sync_stall_recovery"] = true,
                    ["resilience_timing_failsafes"] = true,
                    ["resilience_disconnect_prevention"] = true,
                ["lan_pinned_host"] = "",
                ["lan_host_passphrase"] = "",
                ["lan_debug_logging"] = false
            };
            TryWriteRaw(defaults.ToJsonString(JsonOpts));
            PatchHelper.Log($"Created default config at {ConfigPath}");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Failed to create default config: {ex}");
        }
    }

    public static bool Exists
    {
        get
        {
            try { return File.Exists(ConfigPath); }
            catch { return false; }
        }
    }

    public static bool GetBool(string key, bool fallback = false)
    {
        if (!TryGet(key, out var element))
            return fallback;
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt32(out var value) ? value != 0 : fallback,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var value) ? value : fallback,
            _ => fallback,
        };
    }

    public static int GetInt(string key, int fallback = 0)
    {
        if (!TryGet(key, out var element))
            return fallback;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
            return number;
        return int.TryParse(element.ToString(), out var value) ? value : fallback;
    }

    public static string GetString(string key, string fallback = "")
    {
        if (!TryGet(key, out var element))
            return fallback;
        return element.ValueKind == JsonValueKind.String ? element.GetString() ?? fallback : element.ToString();
    }

    private static readonly object _cacheLock = new();

    public static void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cached?.Dispose();
            _cached = null;
            _lastReadUtc = DateTime.MinValue;
        }
    }

    public static bool TryGet(string key, out JsonElement element)
    {
        element = default;
        try
        {
            lock (_cacheLock)
            {
                var file = new FileInfo(ConfigPath);
                if (!file.Exists)
                    return false;
                if (_cached == null || file.LastWriteTimeUtc > _lastReadUtc)
                {
                    _cached?.Dispose();
                    _cached = JsonDocument.Parse(File.ReadAllText(file.FullName));
                    _lastReadUtc = file.LastWriteTimeUtc;
                }
                if (!_cached.RootElement.TryGetProperty(key, out var raw))
                    return false;
                element = raw.Clone();
                return true;
            }
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Failed to read config '{key}': {exception}");
            return false;
        }
    }

    public static bool TryReadRaw(out string? json)
    {
        json = null;
        try
        {
            var path = ConfigPath;
            if (!File.Exists(path))
                return false;
            json = File.ReadAllText(path);
            return true;
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Failed to read raw config: {exception}");
            return false;
        }
    }

    public static bool TryWriteRaw(string json)
    {
        try
        {
            var path = ConfigPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);
            InvalidateCache();
            return true;
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Failed to write raw config: {exception}");
            return false;
        }
    }

    private static readonly object _writeLock = new();

    /// <summary>
    /// Thread-safe read-modify-write helper. Reads the current config JSON,
    /// passes it to the modifier (which returns a new JSON string), and
    /// writes it back. All under a single lock so concurrent writes from
    /// different code paths (settings UI, join flow, save logic) cannot
    /// silently overwrite each other. This replaces the old pattern of
    /// callers doing TryReadRaw + TryWriteRaw under their own separate locks.
    /// </summary>
    public static bool ModifyRaw(Func<string, string> modifier)
    {
        if (modifier == null) return false;
        lock (_writeLock)
        {
            try
            {
                if (!TryReadRaw(out var json) || string.IsNullOrWhiteSpace(json))
                    json = "{}";
                var result = modifier(json);
                if (string.IsNullOrWhiteSpace(result))
                    return false;
                return TryWriteRaw(result);
            }
            catch (Exception exception)
            {
                PatchHelper.Log($"Failed to modify config: {exception}");
                return false;
            }
        }
    }

    public static bool SetValue(string key, object value)
    {
        return ModifyRaw(json =>
        {
            var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
            root[key] = value switch
            {
                null => null,
                bool b => b,
                int i => i,
                long l => l,
                float f => f,
                double d => d,
                string s => s,
                JsonNode n => n.DeepClone(),
                _ => value.ToString(),
            };
            return root.ToJsonString(JsonOpts);
        });
    }
}