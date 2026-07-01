
using LanSpire.Patches;
namespace LanSpire.Services;

/// <summary>
/// System 1 - Sync stall recovery.
///
/// The game's CombatStateSynchronizer waits for all peers' sync packets
/// with no timeout. On direct-connect (raw UDP, unlike Steam's reliable
/// transport) a lost packet means the wait never ends and the party
/// freezes on combat start. This patch adds a timeout so the wait gives
/// up and proceeds with whatever data arrived.
///
/// Designed to be generalizable: the same timeout pattern can be applied
/// to other synchronizers (map, act, treasure, event) if needed.
/// </summary>
public static class SyncStallRecovery
{
    private const int SyncTimeoutMs = 30_000;

    private static FieldInfo? _tcsField;

    public static void Apply(Harmony harmony)
    {
        if (!LanConfig.GetBool("resilience_sync_stall_recovery", true))
        {
            PatchHelper.Log("SYNC STALL RECOVERY: disabled by config, skipping");
            return;
        }

        _tcsField = typeof(CombatStateSynchronizer)
            .GetField(GameInternals.CombatSync_SyncCompletionSource, BindingFlags.NonPublic | BindingFlags.Instance);

        if (_tcsField == null)
        {
            PatchHelper.Log("SYNC STALL RECOVERY: _syncCompletionSource field not found on CombatStateSynchronizer - skipping (game version mismatch?)");
            return;
        }

        PatchHelper.Patch(harmony, typeof(CombatStateSynchronizer), "StartSync",
            postfix: PatchHelper.Method(typeof(SyncStallRecovery), nameof(StartSyncPostfix)));

        PatchHelper.Log("SYNC STALL RECOVERY: patched CombatStateSynchronizer.StartSync (timeout=30s)");
    }

    /// <summary>
    /// After the game creates _syncCompletionSource in StartSync, start a
    /// background timer. If the TCS is still incomplete after 30 seconds
    /// (a peer's packet was lost or they're unreachable), force-complete
    /// it so WaitForSync stops hanging and combat begins.
    /// </summary>
    private static void StartSyncPostfix(CombatStateSynchronizer __instance)
    {
        if (!LanMultiplayerPatches.IsLanSessionActive)
            return;

        try
        {
            var tcs = _tcsField?.GetValue(__instance) as TaskCompletionSource;
            if (tcs == null)
                return;

            var captured = tcs;
            Task.Run(async () =>
            {
                await Task.Delay(SyncTimeoutMs);
                if (!captured.Task.IsCompleted)
                {
                    PatchHelper.Log(
                        $"SYNC TIMEOUT: Force-completing combat sync after {SyncTimeoutMs}ms" +
                        " - a peer's sync packet was likely lost or they are unreachable.");
                    captured.TrySetResult();
                }
            });
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"SYNC TIMEOUT: error in StartSync postfix: {ex}");
        }
    }


    /// <summary>
    /// PlayerChoiceSynchronizer.WaitForRemoteChoice creates a TCS and
    /// awaits it. If the remote player's choice message is lost (UDP),
    /// the await never completes and combat hangs mid-action.
    /// This postfix wraps the returned Task with the same 30s timeout.
    /// On timeout, returns a PlayerChoiceResult with ChoiceType=None
    /// (safe default - "no choice made") so combat can proceed.
    /// </summary>
    public static void ApplyPlayerChoiceTimeout(Harmony harmony)
    {
        var t = typeof(PlayerChoiceSynchronizer);

        if (!PatchHelper.HasMethod(t, "WaitForRemoteChoice"))
        {
            PatchHelper.Log("SYNC STALL RECOVERY: PlayerChoiceSynchronizer.WaitForRemoteChoice not found - skipping");
            return;
        }

        PatchHelper.Patch(harmony, t, "WaitForRemoteChoice",
            postfix: PatchHelper.Method(typeof(SyncStallRecovery), nameof(WaitForRemoteChoicePostfix)));

        PatchHelper.Log("SYNC STALL RECOVERY: patched PlayerChoiceSynchronizer.WaitForRemoteChoice (timeout=30s)");
    }

    private static void WaitForRemoteChoicePostfix(ref Task<PlayerChoiceResult> __result)
    {
        if (!LanMultiplayerPatches.IsLanSessionActive)
            return;

        var original = __result;
        if (original == null || original.IsCompleted)
            return;

        __result = WrapPlayerChoiceWithTimeout(original);
    }

    private static async Task<PlayerChoiceResult> WrapPlayerChoiceWithTimeout(
        Task<PlayerChoiceResult> original)
    {
        var timeout = Task.Delay(SyncTimeoutMs);
        var winner = await Task.WhenAny(original, timeout);

        if (winner == timeout)
        {
            // Observe the orphaned task to prevent unobserved exception
            _ = original.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    PatchHelper.Log($"Orphaned player choice task failed after timeout: {t.Exception?.InnerException?.Message ?? t.Exception?.Message}");
            }, TaskContinuationOptions.ExecuteSynchronously);

            PatchHelper.Log(
                $"PLAYER CHOICE TIMEOUT: Force-completing remote choice after {SyncTimeoutMs}ms" +
                " - a peer's choice packet was likely lost or they are unreachable. Returning None.");

            return CreateDefaultChoiceResult();
        }

        return await original;
    }

    /// <summary>
    /// Creates a PlayerChoiceResult with ChoiceType=None via reflection,
    /// because the setter is private init-only.
    /// </summary>
    private static PlayerChoiceResult CreateDefaultChoiceResult()
    {
        var result = (PlayerChoiceResult)Activator.CreateInstance(
            typeof(PlayerChoiceResult))!;

        var setter = typeof(PlayerChoiceResult)
            .GetProperty("ChoiceType")?
            .GetSetMethod(nonPublic: true);

        setter?.Invoke(result, new object[] { PlayerChoiceType.None });

        return result;
    }
}