
namespace LanSpire.Services;

/// <summary>
/// System 2 - Direct-connect timing failsafes.
///
/// Direct-connect (raw ENet) delivers data sooner than the official UI
/// expects. Input or cursor data can arrive before the consuming object
/// exists, causing null-reference crashes. These patches add defensive
/// guards so the game doesn't crash when data arrives early.
///
/// Based on the approach used by TasteSteak's SystemPreheater and
/// RemoteMouseCursorFailsafePatch, reimplemented from understanding
/// (not copied - TasteSteak is CC BY-NC).
/// </summary>
public static class TimingFailsafes
{
    private static FieldInfo? _syncField;
    private static MethodInfo? _getCursorMethod;
    private static MethodInfo? _addCursorMethod;

    public static void Apply(Harmony harmony)
    {
        if (!LanConfig.GetBool("resilience_timing_failsafes", true))
        {
            PatchHelper.Log("TIMING FAILSAFES: disabled by config, skipping");
            return;
        }

        _syncField = typeof(NRemoteMouseCursorContainer)
            .GetField(GameInternals.Sync_Synchronizer, BindingFlags.NonPublic | BindingFlags.Instance);
        _getCursorMethod = typeof(NRemoteMouseCursorContainer)
            .GetMethod(GameInternals.Sync_GetCursor, BindingFlags.NonPublic | BindingFlags.Instance);
        _addCursorMethod = typeof(NRemoteMouseCursorContainer)
            .GetMethod(GameInternals.Sync_AddCursor, BindingFlags.NonPublic | BindingFlags.Instance);

        if (_syncField != null)
        {
            PatchHelper.Patch(harmony, typeof(NRemoteMouseCursorContainer), "OnInputStateChanged",
                prefix: PatchHelper.Method(typeof(TimingFailsafes), nameof(OnInputStateChangedPrefix)));
            PatchHelper.Log("TIMING FAILSAFES: patched NRemoteMouseCursorContainer.OnInputStateChanged (cursor guard)");
        }
        else
        {
            PatchHelper.Log("TIMING FAILSAFES: _synchronizer field not found on NRemoteMouseCursorContainer - cursor guard skipped");
        }

    }

    /// <summary>
    /// Guard OnInputStateChanged: skip if the container is not valid,
    /// not in the tree, or _synchronizer is null (not yet initialized
    /// or already deinitialized). If the cursor for this player doesn't
    /// exist yet, create it before the original method runs.
    /// </summary>
    private static bool OnInputStateChangedPrefix(NRemoteMouseCursorContainer __instance, ulong playerId)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(__instance) || !__instance.IsInsideTree())
                return false;

            var synchronizer = _syncField?.GetValue(__instance);
            if (synchronizer == null)
            {
                PatchHelper.Log($"CURSOR GUARD: _synchronizer null for player {playerId}, skipping OnInputStateChanged");
                return false;
            }

            if (_getCursorMethod != null && _addCursorMethod != null)
            {
                var cursor = _getCursorMethod.Invoke(__instance, new object[] { playerId });
                if (cursor == null)
                {
                    PatchHelper.Log($"CURSOR GUARD: cursor for player {playerId} missing, creating via AddCursor");
                    _addCursorMethod.Invoke(__instance, new object[] { playerId });
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"CURSOR GUARD: error: {ex}");
        }

        return true;
    }
}