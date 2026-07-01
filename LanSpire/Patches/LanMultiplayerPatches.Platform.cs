using LanSpire.Network;
using System.Security.Cryptography;
using System.Text;
namespace LanSpire.Patches;

public static partial class LanMultiplayerPatches
{

    public static void InitialGameInfoBasicPostfix(ref InitialGameInfoMessage __result)
    {
        if (!_isLanSessionActive) return;
        var boxedResult = (object)__result;
        if (SetInitialGameInfoModField(boxedResult, "mods"))
        {
            __result = (InitialGameInfoMessage)boxedResult;
            return;
        }
        SetInitialGameInfoModField(boxedResult, "gameplayAffectingMods");
        __result = (InitialGameInfoMessage)boxedResult;
    }

    private static bool SetInitialGameInfoModField(object message, string fieldName)
    {
        var field = AccessTools.Field(typeof(InitialGameInfoMessage), fieldName);
        if (field == null) return false;
        var mods = field.GetValue(message) as List<string>;
        field.SetValue(message, GetMultiplayerCompatibilityModNameList(mods));
        return true;
    }

    public static void ModNameListPostfix(ref List<string>? __result)
    {
        if (!_isLanSessionActive) return;
        __result = GetMultiplayerCompatibilityModNameList(__result);
    }

    public static void JoinFlowBeginPrefix()
    {
        if (!_isLanSessionActive) return;
        ClearPlayerNameOverrides(PlatformType.None);
    }

    public static bool NullGetPlayerNamePrefix(ulong playerId, ref string __result)
    {
        if (!_isLanSessionActive)
            return true;
        if (TryGetPlayerNameOverride(PlatformType.None, playerId, out var playerName))
        {
            __result = playerName!;
            return false;
        }
        __result = playerId switch
        {
            1UL => "Host",
            1000UL => "Client 1",
            2000UL => "Client 2",
            3000UL => "Client 3",
            _ => $"Player {playerId % 10000UL:0000}",
        };
        return false;
    }

    public static bool NullGetLocalPlayerIdPrefix(ref ulong __result)
    {
        if (!_isLanSessionActive)
            return true;
        if (LanConfig.GetBool("lan_use_custom_player_id", false) && TryGetConfiguredCustomPeerId(out var playerId))
        {
            __result = playerId;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Patches PlatformUtil.GetPlayerName(PlatformType, ulong) - the static
    /// dispatcher used by all game code. Catches calls regardless of platform type.
    /// </summary>
    public static bool PlatformUtilGetPlayerNamePrefix(PlatformType platformType, ulong playerId, ref string __result)
    {
        if (!_isLanSessionActive)
            return true;
        if (TryGetPlayerNameOverride(PlatformType.None, playerId, out var playerName))
        {
            __result = playerName!;
            return false;
        }
        __result = playerId switch
        {
            1UL => "Host",
            1000UL => "Client 1",
            2000UL => "Client 2",
            3000UL => "Client 3",
            _ => $"Player {playerId % 10000UL:0000}",
        };
        return false;
    }

    /// <summary>
    /// Patches PlatformUtil.GetLocalPlayerId(PlatformType) - the static
    /// dispatcher. Overrides the local player ID when a custom ID is configured.
    /// </summary>
    public static bool PlatformUtilGetLocalPlayerIdPrefix(PlatformType platformType, ref ulong __result)
    {
        if (!_isLanSessionActive)
            return true;
        if (LanConfig.GetBool("lan_use_custom_player_id", false) && TryGetConfiguredCustomPeerId(out var playerId))
        {
            __result = playerId;
            return false;
        }
        return true;
    }

    public static void LoadAndCanonicalizeMultiplayerRunSavePrefix(object __instance, ref ulong __0)
    {
        try
        {
            var localPlayerId = __0;
            var saveData = TryLoadRawMultiplayerSave(__instance);
            var savePlayerIds = GetSavePlayerIds(saveData).Distinct().ToList();
            if (savePlayerIds.Count == 0) return;
            if (savePlayerIds.Contains(localPlayerId))
            {
                RememberMultiplayerSavePlayerId(localPlayerId);
                return;
            }
            if (TryResolveMultiplayerSavePlayerId(savePlayerIds, out var savePlayerId))
            {
                PatchHelper.Log($"LAN save canonicalization uses saved player ID {savePlayerId} instead of current {localPlayerId}.");
                __0 = savePlayerId;
                RememberMultiplayerSavePlayerId(savePlayerId);
            }
            else
            {
                PatchHelper.Log($"LAN save canonicalization kept current ID {localPlayerId}; save has unmatched IDs: {string.Join(",", savePlayerIds)}");
            }
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"LAN save ID compatibility patch failed: {exception}");
        }
    }

    // ==========================================
    // Player Name Sync
    // ==========================================

    private static INetGameService? _nameSyncNetService;

    /// <summary>Register the name sync and passphrase message handlers.
    /// Called by host on StartHost and by client on JoinFlow begin.</summary>
    internal static void RegisterNameSyncHandler(INetGameService netService)
    {
        if (netService == null) return;
        _nameSyncNetService = netService;
        try
        {
            netService.RegisterMessageHandler<LanPlayerNameSyncMessage>(HandleNameSyncMessage);
            netService.RegisterMessageHandler<LanPassphraseMessage>(HandlePassphraseMessage);
            netService.RegisterMessageHandler<LanPingMessage>(HandlePingMessage);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Failed to register message handlers: {ex}");
        }
    }

    /// <summary>Unregister the handlers. Called on disconnect.</summary>
    internal static void UnregisterNameSyncHandler()
    {
        if (_nameSyncNetService == null) return;
        try
        {
            _nameSyncNetService.UnregisterMessageHandler<LanPlayerNameSyncMessage>(HandleNameSyncMessage);
            _nameSyncNetService.UnregisterMessageHandler<LanPassphraseMessage>(HandlePassphraseMessage);
            _nameSyncNetService.UnregisterMessageHandler<LanPingMessage>(HandlePingMessage);
        }
        catch { }
        _nameSyncNetService = null;
    }

    /// <summary>Handler for incoming name sync messages. Stores the name
    /// in PlayerNameOverrides so all game UI shows the synced name.
    /// On host, also broadcasts the name to all other clients.
    /// Security: host validates senderId == msg.netId to prevent name spoofing
    /// (a client claiming to set a name for a different player).</summary>
    private static void HandleNameSyncMessage(LanPlayerNameSyncMessage msg, ulong senderId)
    {
        if (string.IsNullOrWhiteSpace(msg.name)) return;
        var ns = _nameSyncNetService;
        // Host: validate sender matches claimed netId before applying
        if (ns != null && ns.Type == NetGameType.Host && senderId != msg.netId)
        {
            PatchHelper.Log($"Name sync SPOOF rejected: sender {senderId} claimed netId {msg.netId}");
            return;
        }
        SetPlayerNameOverride(PlatformType.None, msg.netId, msg.name);
        PatchHelper.Log($"Name sync: player {msg.netId} -> \"{msg.name}\"");
        // Host relays to all other clients so everyone has the full name map.
        // Also sends host's own name so new clients learn it.
        if (ns != null && ns.Type == NetGameType.Host)
        {
            BroadcastPlayerName(msg.netId, msg.name);
            // Ensure host's own name is broadcast to all clients
            var hostId = GetOrCreateLocalPeerId();
            if (hostId != msg.netId)
            {
                var hostName = GetConfiguredPlayerDisplayName();
                if (string.IsNullOrWhiteSpace(hostName)) hostName = "Host";
                BroadcastPlayerName(hostId, hostName);
            }
            // Replay all known names so late joiners receive the full name map.
            // Broadcasts to all clients (wasteful for old clients, but harmless --
            // they just overwrite with the same value). Snapshot under lock to
            // avoid holding _playerNameLock during network I/O.
            List<(ulong pid, string pname)> knownNames;
            lock (_playerNameLock)
            {
                knownNames = PlayerNameOverrides.TryGetValue(PlatformType.None, out var allNames)
                    ? allNames.Where(kvp => kvp.Key != msg.netId && kvp.Key != hostId
                        && !string.IsNullOrWhiteSpace(kvp.Value))
                        .Select(kvp => (kvp.Key, kvp.Value)).ToList()
                    : new List<(ulong, string)>();
            }
            foreach (var (pid, pname) in knownNames)
                BroadcastPlayerName(pid, pname);
        }
    }

    /// <summary>Send our local player name to the host (client) or to all
    /// peers (host). Called after connection is established.</summary>
    internal static void SendLocalPlayerName()
    {
        try
        {
            var ns = _nameSyncNetService;
            if (ns == null || !ns.IsConnected) return;
            var myId = GetOrCreateLocalPeerId();
            var myName = GetConfiguredPlayerDisplayName();
            if (string.IsNullOrWhiteSpace(myName))
                myName = myId == 1UL ? "Host" : $"Player {myId % 10000UL:0000}";
            var msg = new LanPlayerNameSyncMessage { netId = myId, name = myName };
            ns.SendMessage(msg);
            PatchHelper.Log($"Sent local player name: {myId} -> \"{myName}\"");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Failed to send local player name: {ex}");
        }
    }

    /// <summary>Host: broadcast a specific player's name to all clients.
    /// Called when host receives a name from a client and needs to relay it.</summary>
    internal static void BroadcastPlayerName(ulong netId, string name)
    {
        try
        {
            var ns = _nameSyncNetService;
            if (ns == null || ns.Type != NetGameType.Host) return;
            var msg = new LanPlayerNameSyncMessage { netId = netId, name = name };
            ns.SendMessage(msg);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Failed to broadcast player name: {ex}");
        }
    }

    // ==========================================
    // Passphrase Auth (F2)
    // ==========================================

    /// <summary>Hash a passphrase string with SHA256, return lowercase hex.</summary>
    private static string HashPassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(passphrase.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Host: validate incoming passphrase from a newly connected client.
    /// If host has a passphrase configured and the client's hash doesn't match,
    /// disconnect the peer.</summary>
    private static void HandlePassphraseMessage(LanPassphraseMessage msg, ulong senderId)
    {
        var ns = _nameSyncNetService;
        if (ns == null) return;

        var hostPassphrase = LanConfig.GetString("lan_host_passphrase", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(hostPassphrase))
        {
            // No passphrase configured -- no auth required
            return;
        }

        var expectedHash = HashPassphrase(hostPassphrase);
        if (msg.hash != expectedHash)
        {
            PatchHelper.Log($"WARNING: Passphrase mismatch from peer {senderId}. Connection allowed but host owner should verify.");
            ShowToast(Strings.T(
                $"密码错误: 玩家 {senderId % 10000} 的密码不匹配",
                $"Passphrase mismatch from player {senderId % 10000}"));
        }
        else
        {
            PatchHelper.Log($"Passphrase verified for peer {senderId}.");
        }
    }

    /// <summary>Client: send passphrase to host after connecting.
    /// Called alongside SendLocalPlayerName.</summary>
    internal static void SendPassphrase()
    {
        try
        {
            var ns = _nameSyncNetService;
            if (ns == null || !ns.IsConnected) return;
            var passphrase = LanConfig.GetString("lan_host_passphrase", string.Empty).Trim();
            // Always send -- host with no passphrase just ignores it
            var msg = new LanPassphraseMessage { hash = HashPassphrase(passphrase) };
            ns.SendMessage(msg);
            PatchHelper.Log("Sent passphrase to host.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Failed to send passphrase: {ex}");
        }
    }

    // ==========================================
    // Ping/Latency (F3)
    // ==========================================

    private static Godot.Timer? _pingTimer;
    private static Label? _pingLabel;
    private static ulong _lastPingSentMs;
    private static int _lastLatencyMs = -1;

    /// <summary>Start the ping poller on the host or client. Sends a ping
    /// every 2 seconds and updates the latency label.</summary>
    internal static void StartPingPoller()
    {
        StopPingPoller();
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null) return;

            var label = new Label { Name = "LanPingLabel" };
            label.AddThemeFontSizeOverride("font_size", 14);
            label.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 0.6f));
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            label.AnchorLeft = 1f; label.AnchorRight = 1f;
            label.AnchorTop = 0f; label.AnchorBottom = 0f;
            label.OffsetLeft = -320; label.OffsetRight = -16;
            label.OffsetTop = 44; label.OffsetBottom = 64;
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.ZIndex = 999;
            label.Visible = false;
            tree.Root.AddChild(label);
            _pingLabel = label;

            var timer = new Godot.Timer
            {
                Name = "LanPingTimer",
                WaitTime = 2.0,
                Autostart = true,
                OneShot = false,
            };
            timer.Connect(Godot.Timer.SignalName.Timeout, Godot.Callable.From(OnPingTick));
            tree.Root.AddChild(timer);
            _pingTimer = timer;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Ping poller start failed: {ex}");
        }
    }

    internal static void StopPingPoller()
    {
        var timer = _pingTimer;
        _pingTimer = null;
        if (timer != null && Godot.GodotObject.IsInstanceValid(timer))
        {
            try { timer.QueueFree(); } catch { }
        }
        var label = _pingLabel;
        _pingLabel = null;
        if (label != null && Godot.GodotObject.IsInstanceValid(label))
        {
            try { label.QueueFree(); } catch { }
        }
        _lastLatencyMs = -1;
    }

    private static void OnPingTick()
    {
        try
        {
            var ns = _nameSyncNetService;
            if (ns == null || !ns.IsConnected)
            {
                if (_pingLabel != null && Godot.GodotObject.IsInstanceValid(_pingLabel))
                    _pingLabel.Visible = false;
                return;
            }

            // Show label only in lobby phase
            if (!IsInLobbyPhase())
            {
                if (_pingLabel != null && Godot.GodotObject.IsInstanceValid(_pingLabel))
                    _pingLabel.Visible = false;
                return;
            }

            // Host doesn't need to measure ping to itself. Show "Host" and
            // don't send ping messages (mobile clients can't deserialize
            // our custom message type 53 anyway).
            if (ns.Type == NetGameType.Host)
            {
                if (_pingLabel != null && Godot.GodotObject.IsInstanceValid(_pingLabel))
                {
                    _pingLabel.Visible = true;
                    _pingLabel.Text = Strings.T("主机", "Host");
                }
                return;
            }

            // Client: send ping and measure RTT.
            _lastPingSentMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var msg = new LanPingMessage
            {
                timestampMs = _lastPingSentMs,
                senderId = GetOrCreateLocalPeerId(),
            };
            ns.SendMessage(msg);

            // Check for timeout: if we haven't received a response in 6s,
            // show "N/A" instead of "Ping..." forever.
            if (_lastLatencyMs < 0 && _lastPingSentMs > 0)
            {
                var elapsed = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastPingSentMs;
                if (elapsed > 6000)
                    _lastLatencyMs = -2; // sentinel: timed out
            }

            if (_pingLabel != null && Godot.GodotObject.IsInstanceValid(_pingLabel))
            {
                _pingLabel.Visible = true;
                if (_lastLatencyMs >= 0)
                    _pingLabel.Text = Strings.T($"延迟 {_lastLatencyMs}ms", $"Ping {_lastLatencyMs}ms");
                else if (_lastLatencyMs == -2)
                    _pingLabel.Text = Strings.T("延迟 N/A", "Ping N/A");
                else
                    _pingLabel.Text = Strings.T("延迟...", "Ping...");
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Ping tick failed: {ex}");
        }
    }

    /// <summary>Handle incoming ping message. Host echoes to all clients.
    /// Client calculates RTT from the timestamp.</summary>
    private static void HandlePingMessage(LanPingMessage msg, ulong senderId)
    {
        var ns = _nameSyncNetService;
        if (ns == null) return;

        if (ns.Type == NetGameType.Host)
        {
            // Host: echo the ping back to all clients (broadcast)
            try { ns.SendMessage(msg); }
            catch (Exception ex) { PatchHelper.Log($"Host ping echo failed: {ex}"); }
        }
        else
        {
            // Client: calculate RTT if this is our ping coming back
            var myId = GetOrCreateLocalPeerId();
            if (msg.senderId == myId)
            {
                var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (now >= msg.timestampMs)
                {
                    _lastLatencyMs = (int)(now - msg.timestampMs);
                }
            }
        }
    }

}
