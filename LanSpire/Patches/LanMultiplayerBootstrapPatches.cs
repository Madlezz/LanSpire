
namespace LanSpire.Patches;

/// <summary>
/// Defers LAN patch application until main menu is ready.
/// Same pattern as the Android version - game types must be
/// fully initialized before patching networking methods.
/// </summary>
public static class LanMultiplayerBootstrapPatches
{
    private static Harmony? _harmony;
    private static bool _lanApplied;

    public static void Apply(Harmony harmony)
    {
        _harmony = harmony;
        PatchHelper.Patch(harmony, typeof(NMainMenu), "_Ready",
            postfix: PatchHelper.Method(typeof(LanMultiplayerBootstrapPatches), nameof(MainMenuReadyPostfix)));
        PatchHelper.Log("LAN multiplayer patches deferred until main menu is ready.");
    }

    public static void MainMenuReadyPostfix()
    {
        if (_lanApplied)
            return;
        _lanApplied = true;
        try
        {
            PatchHelper.Log("Applying deferred LAN multiplayer patches.");
            if (_harmony != null)
                LanMultiplayerPatches.Apply(_harmony);
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Deferred LAN multiplayer patch application failed: {exception}");
        }
    }
}