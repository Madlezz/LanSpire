using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LanSpire.Patches;

namespace LanSpire.Services;

/// <summary>
/// Checks GitHub Releases API for a newer LanSpire version.
/// Fires a toast on the main menu if an update is available.
/// Runs at most once per game session.
/// </summary>
internal static class UpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/Madlezz/LanSpire/releases/latest";
    private static bool _checked;

    public static async Task CheckAsync()
    {
        if (_checked) return;
        _checked = true;

        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LanSpire-UpdateCheck");
            http.Timeout = System.TimeSpan.FromSeconds(5);

            var response = await http.GetStringAsync(ReleasesUrl);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("tag_name", out var tagEl)) return;
            var latestTag = tagEl.GetString();
            if (string.IsNullOrWhiteSpace(latestTag)) return;

            var currentVersion = LanMultiplayerPatches.ModVersion;
            if (IsNewerVersion(latestTag, currentVersion))
            {
                PatchHelper.Log($"Update available: {latestTag} (current: {currentVersion})");
                var msg = Strings.T(
                    $"LanSpire 有新版本: {latestTag}",
                    $"LanSpire update available: {latestTag}");
                LanMultiplayerPatches.ShowUpdateToast(msg);
            }
        }
        catch (System.Exception ex)
        {
            PatchHelper.Log($"Update check failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Simple semver-ish comparison: "v0.8.0" > "v0.7.0" => true.
    /// Strips 'v' prefix, parses major.minor.patch as ints.
    /// </summary>
    private static bool IsNewerVersion(string latest, string current)
    {
        if (!TryParseVersion(latest, out var l)) return false;
        if (!TryParseVersion(current, out var c)) return false;
        if (l.major != c.major) return l.major > c.major;
        if (l.minor != c.minor) return l.minor > c.minor;
        return l.patch > c.patch;
    }

    private static bool TryParseVersion(string tag, out (int major, int minor, int patch) version)
    {
        version = (0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var s = tag.TrimStart('v', 'V');
        var parts = s.Split('.');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out var major)) return false;
        if (!int.TryParse(parts[1], out var minor)) return false;
        var patch = 0;
        if (parts.Length >= 3) int.TryParse(parts[2], out patch);
        version = (major, minor, patch);
        return true;
    }
}
