
using LanSpire.Patches;
namespace LanSpire.Services;

/// <summary>
/// System 3 (minimal) - Anti-deadlock stand-in.
///
/// When a player disconnects mid-combat, the game waits forever for
/// their "ready to end turn" / "ready to begin enemy turn" signal.
/// This patch detects disconnects via RunLobby events, waits 8 seconds
/// (to ride out brief network hiccups), then supplies default "ready"
/// actions for the confirmed-gone player so combat proceeds.
///
/// This is the MINIMAL tier: no smart card play, no AI. Just breaks
/// the deadlock by marking the disconnected player as ready. The full
/// AI takeover tier (TasteSteak-style) is deferred until the minimal
/// tier proves insufficient in real testing.
/// </summary>
public static class DisconnectPrevention
{
    private const int DisconnectDelayMs = 8_000;

    private static FieldInfo? _stateField;
    private static MethodInfo? _setReadyToEndTurnMethod;
    private static MethodInfo? _setReadyToBeginEnemyTurnMethod;
    private static PropertyInfo? _playersProperty;

    private static readonly Dictionary<ulong, long> _disconnectTimestamps = new();
    private static readonly object _lock = new();

    public static void Apply(Harmony harmony)
    {
        if (!LanConfig.GetBool("resilience_disconnect_prevention", true))
        {
            PatchHelper.Log("DISCONNECT PREVENTION: disabled by config, skipping");
            return;
        }

        PatchHelper.Patch(harmony, typeof(RunLobby), "OnDisconnectedFromClientAsHost",
            prefix: PatchHelper.Method(typeof(DisconnectPrevention), nameof(OnDisconnectedFromClientAsHostPrefix)));
        PatchHelper.Log("DISCONNECT PREVENTION: patched RunLobby.OnDisconnectedFromClientAsHost");

        if (PatchHelper.HasMethod(typeof(RunLobby), "OnConnectedToClientAsHost"))
        {
            PatchHelper.Patch(harmony, typeof(RunLobby), "OnConnectedToClientAsHost",
                prefix: PatchHelper.Method(typeof(DisconnectPrevention), nameof(OnConnectedToClientAsHostPrefix)));
            PatchHelper.Log("DISCONNECT PREVENTION: patched RunLobby.OnConnectedToClientAsHost (rejoin detection)");
        }
        else
        {
            PatchHelper.Log("DISCONNECT PREVENTION: OnConnectedToClientAsHost not found - rejoin detection disabled");
        }

        _stateField = typeof(CombatManager).GetField(GameInternals.CombatManager_State, BindingFlags.NonPublic | BindingFlags.Instance);
        _setReadyToEndTurnMethod = typeof(CombatManager).GetMethod("SetReadyToEndTurn",
            BindingFlags.Public | BindingFlags.Instance);
        _setReadyToBeginEnemyTurnMethod = typeof(CombatManager).GetMethod("SetReadyToBeginEnemyTurn",
            BindingFlags.Public | BindingFlags.Instance);

        if (_stateField == null)
            PatchHelper.Log("DISCONNECT PREVENTION: _state field not found on CombatManager");
        if (_setReadyToEndTurnMethod == null)
            PatchHelper.Log("DISCONNECT PREVENTION: SetReadyToEndTurn not found on CombatManager");
        if (_setReadyToBeginEnemyTurnMethod == null)
            PatchHelper.Log("DISCONNECT PREVENTION: SetReadyToBeginEnemyTurn not found on CombatManager");


        PatchHelper.Log("DISCONNECT PREVENTION: patches applied (8s delay before deadlock break)");
    }

    /// <summary>
    /// Prefix on RunLobby.OnDisconnectedFromClientAsHost -- fires when a
    /// remote player disconnects from the host. Triggers the 8-second
    /// grace period before breaking any combat deadlock.
    /// Signature matches: (ulong playerId, NetErrorInfo info) -- we ignore info.
    /// </summary>
    private static void OnDisconnectedFromClientAsHostPrefix(ulong playerId, object __1)
    {
        OnPlayerDisconnected(playerId);
    }

    /// <summary>
    /// Prefix on RunLobby.OnConnectedToClientAsHost -- fires when a
    /// player (re)connects to the host. Clears the disconnected state
    /// so the 8s deadlock-break timer is cancelled for this player.
    /// Signature matches: (ulong playerId) -- only one param.
    /// </summary>
    private static void OnConnectedToClientAsHostPrefix(ulong playerId)
    {
        OnPlayerRejoined(playerId);
    }

    /// <summary>
    /// Called when a player disconnects. Start an 8-second timer; if the
    /// player is still gone after that, break the deadlock by supplying
    /// default ready/end-turn actions.
    /// </summary>
    private static void OnPlayerDisconnected(ulong playerId)
    {
        if (!LanMultiplayerPatches.IsLanSessionActive)
            return;

        var timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (_lock)
        {
            _disconnectTimestamps[playerId] = timestamp;
        }

        PatchHelper.Log($"DISCONNECT PREVENTION: Player {playerId} disconnected, starting {DisconnectDelayMs}ms grace period (ts={timestamp})");

        Task.Run(async () =>
        {
            await Task.Delay(DisconnectDelayMs);
            await TryBreakDeadlockAsync(playerId, timestamp);
        });
    }

    /// <summary>Called when a player rejoins - clear their disconnected state.</summary>
    private static void OnPlayerRejoined(ulong playerId)
    {
        lock (_lock)
        {
            _disconnectTimestamps.Remove(playerId);
        }
        PatchHelper.Log($"DISCONNECT PREVENTION: Player {playerId} rejoined, cleared disconnect state");
    }

    /// <summary>
    /// After the grace period, if the player is still disconnected (same
    /// disconnect event - no newer reconnect+disconnect cycle) and
    /// combat is in progress, call SetReadyToEndTurn and
    /// SetReadyToBeginEnemyTurn for them so the game doesn't hang.
    /// The timestamp parameter ensures we only act on the ORIGINAL disconnect
    /// event. If the player reconnected and disconnected again (flapping),
    /// a newer timestamp will be in the dictionary, and this older watchdog
    /// will see the mismatch and abort.
    /// </summary>
    private static async Task TryBreakDeadlockAsync(ulong playerId, long expectedTimestamp)
    {
        lock (_lock)
        {
            if (!_disconnectTimestamps.TryGetValue(playerId, out var currentTimestamp))
            {
                PatchHelper.Log($"DISCONNECT PREVENTION: Player {playerId} reconnected within grace period, no action needed");
                return;
            }
            if (currentTimestamp != expectedTimestamp)
            {
                PatchHelper.Log($"DISCONNECT PREVENTION: Player {playerId} had a newer disconnect event (ts={currentTimestamp} vs expected {expectedTimestamp}), this watchdog is stale - aborting");
                return;
            }
        }

        var cm = CombatManager.Instance;
        if (cm == null)
        {
            PatchHelper.Log($"DISCONNECT PREVENTION: No CombatManager instance available, cannot break deadlock for player {playerId}");
            return;
        }

        if (!cm.IsInProgress)
        {
            PatchHelper.Log($"DISCONNECT PREVENTION: Combat not in progress, no deadlock to break for player {playerId}");
            return;
        }

        Player? player = null;
        try
        {
            var state = _stateField?.GetValue(cm);
            if (state != null)
            {
                _playersProperty ??= state.GetType().GetProperty("Players");
                var players = _playersProperty?.GetValue(state) as IEnumerable<Player>;
                player = players?.FirstOrDefault(p => p.NetId == playerId);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"DISCONNECT PREVENTION: Error finding Player {playerId}: {ex}");
        }

        if (player == null)
        {
            PatchHelper.Log($"DISCONNECT PREVENTION: Player {playerId} not found in combat state, cannot break deadlock");
            return;
        }

        try
        {
            PatchHelper.Log($"DISCONNECT PREVENTION: Breaking deadlock for player {playerId} - supplying default ready/end-turn");

            _setReadyToEndTurnMethod?.Invoke(cm, new object?[] { player, false, null });
            _setReadyToBeginEnemyTurnMethod?.Invoke(cm, new object?[] { player, null });
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"DISCONNECT PREVENTION: Error calling SetReady for player {playerId}: {ex}");
        }
    }
}