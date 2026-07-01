
namespace LanSpire.Localization;

/// <summary>
/// Bilingual helper. Players whose game language is Chinese (zh/zhs) see the
/// first argument; everyone else sees English. Do NOT remove the Chinese strings.
/// </summary>
internal static class Strings
{
    internal static string T(string zh, string en) => IsZh() ? zh : en;

    internal static bool IsZh()
    {
        try
        {
            var locManagerType = typeof(NGame).Assembly.GetType("MegaCrit.Sts2.Core.Localization.LocManager");
            var instance = locManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var language = locManagerType?.GetProperty("Language", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) as string ?? string.Empty;
            return language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || language.StartsWith("zhs", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
