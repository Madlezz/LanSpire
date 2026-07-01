namespace LanSpire.Patches;

public static partial class LanMultiplayerPatches
{
    public const ushort DefaultPort = 33771;
    private const int DefaultMaxPlayers = 4;
    private const int SteamMaxPlayers = 250;
    private const int MessageTypeCount = 54;
    public const string ModVersion = "v0.10.0";

    /// <summary>
    /// Log only when lan_debug_logging is true in config. Use for verbose
    /// diagnostics that would otherwise clutter the log file users share
    /// for support. PatchHelper.Log is always logged (non-gated).
    /// </summary>
    private static void DebugLog(string message)
    {
        if (LanConfig.GetBool("lan_debug_logging", false))
            PatchHelper.Log(message);
    }
    private const string LanPlayerIdSettingKey = "lan_player_id";
    private const string LanMultiplayerSavePlayerIdSettingKey = "lan_multiplayer_save_player_id";
    private static volatile bool _isJoining;
    private static volatile bool _isScanning;

    /// <summary>
    /// Runtime flag: true when the user entered multiplayer through a LAN button
    /// ("Host LAN" or "Join LAN"). Set when the LAN button is clicked, cleared
    /// when the user returns to the multiplayer landing page or the session ends.
    /// Infrastructure patches (message types, player names, save handling) only
    /// fire when this is true, so Steam multiplayer is untouched.
    /// </summary>
    private static volatile bool _isLanSessionActive;

    internal static bool IsLanSessionActive => _isLanSessionActive;

    private static bool IsLanSessionInFlight()
        => _hostService != null || _discoveryCts != null || _discoveryTask != null;

    private static readonly Dictionary<PlatformType, Dictionary<ulong, string>> PlayerNameOverrides = new();
    private static readonly Dictionary<Type, int> StableMessageTypeIds = new();
    private static readonly Dictionary<int, Type> StableMessageIdTypes = new();
    private static readonly string[] StableMessageTypeOrder =
    {
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.ActionEnqueuedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.CardRemovedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums.ChecksumDataMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums.StateDivergenceMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.CrystalSphereRewardsMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor.ClearMapDrawingsMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor.EndTurnPingMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor.MapDrawingMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor.MapDrawingModeChangedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor.MapPingMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor.ReactionMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor.RestSiteOptionHoveredMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.HookActionEnqueuedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.MerchantCardRemovalMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.PlayerChoiceMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.RequestEnqueueActionMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.RequestEnqueueHookActionMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.RequestResumeActionAfterPlayerChoiceMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.ResumeActionAfterPlayerChoiceMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.RunAbandonedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.GoldLostMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.OptionIndexChosenMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.PeerInputMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.RewardObtainedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.RewardSelectedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.RewardSetSkippedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.SharedEventOptionChosenMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.VotedForSharedEventOptionMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.SyncPlayerDataMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.SyncRngMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.TreasureChestOpenedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.HeartbeatRequestMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.HeartbeatResponseMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLoadJoinRequestMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLoadJoinResponseMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinRequestMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientRejoinRequestMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientRejoinResponseMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.InitialGameInfoMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyAscensionChangedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyBeginLoadedRunMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyBeginRunMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyModifiersChangedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerChangedCharacterMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerSetReadyMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbySeedChangedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerJoinedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerLeftMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerReconnectedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerRejoinedMessage",
        "LanSpire.Network.LanPlayerNameSyncMessage",
        "LanSpire.Network.LanPassphraseMessage",
        "LanSpire.Network.LanPingMessage",
    };

    public static void Apply(Harmony harmony)
    {
        if (!ShouldApplyLocalLanPatches())
            return;

        BuildMessageTypeMaps();

        PatchHelper.Patch(harmony, typeof(InitialGameInfoMessage), "Basic",
            postfix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(InitialGameInfoBasicPostfix)));
        PatchHelper.Patch(harmony, typeof(ModManager), "GetGameplayRelevantModNameList",
            postfix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(ModNameListPostfix)));
        PatchHelper.Patch(harmony, typeof(ModManager), "GetModNameList",
            postfix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(ModNameListPostfix)));
        PatchHelper.Patch(harmony, typeof(JoinFlow), "Begin",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(JoinFlowBeginPrefix)));
        PatchHelper.Patch(harmony, typeof(NullPlatformUtilStrategy), "GetPlayerName",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(NullGetPlayerNamePrefix)));
        PatchHelper.Patch(harmony, typeof(NullPlatformUtilStrategy), "GetLocalPlayerId",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(NullGetLocalPlayerIdPrefix)));

        var platformUtilType = typeof(NullPlatformUtilStrategy).Assembly.GetType("MegaCrit.Sts2.Core.Platform.PlatformUtil");
        if (platformUtilType != null)
        {
            PatchHelper.Patch(harmony, platformUtilType, "GetPlayerName",
                prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(PlatformUtilGetPlayerNamePrefix)));
            PatchHelper.Patch(harmony, platformUtilType, "GetLocalPlayerId",
                prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(PlatformUtilGetLocalPlayerIdPrefix)));
        }
        PatchMultiplayerSaveCanonicalization(harmony);
        var settingsScreenType = typeof(NJoinFriendScreen).Assembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsScreen");
        if (settingsScreenType != null)
            PatchHelper.Patch(harmony, settingsScreenType, "OnSubmenuShown",
                postfix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(SettingsScreenReadyPostfix)));
        PatchHelper.Patch(harmony, typeof(MessageTypes), "ToId",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(MessageToIdPrefix)));
        PatchHelper.Patch(harmony, typeof(MessageTypes), "TryGetMessageType",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(TryGetMessageTypePrefix)));
        PatchHelper.Patch(harmony, typeof(NetMessageBus), "TryDeserializeMessage",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(TryDeserializeMessagePrefix)));
        PatchHelper.Patch(harmony, typeof(NJoinFriendScreen), "OnSubmenuOpened",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(JoinFriendOpenedPrefix)));
        PatchHelper.Patch(harmony, typeof(NJoinFriendScreen), "RefreshButtonClicked",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(JoinFriendRefreshButtonPrefix)));
        PatchHelper.Patch(harmony, typeof(NJoinFriendScreen), "OnSubmenuClosed",
            postfix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(JoinFriendClosedPostfix)));
        PatchHelper.Patch(harmony, typeof(NMultiplayerHostSubmenu), "StartHostAsync",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(HostSubmenuStartHostAsyncPrefix)));
        PatchHelper.Patch(harmony, typeof(NMultiplayerSubmenu), "StartHostAsync",
            prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(MultiplayerSubmenuStartHostAsyncPrefix)));
        PatchHelper.Patch(harmony, typeof(NMultiplayerSubmenu), "OnSubmenuShown",
            postfix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(MultiplayerSubmenuShownPostfix)));
        PatchHelper.Patch(harmony, typeof(NMultiplayerSubmenu), "UpdateButtons",
            postfix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(UpdateButtonsPostfix)));

        PatchHelper.Patch(harmony, typeof(NetHostGameService), "Disconnect",
            postfix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(HostServiceDisconnectPostfix)));
        var runManagerType = typeof(SerializableRun).Assembly.GetType("MegaCrit.Sts2.Core.Runs.RunManager");
        if (runManagerType != null)
            PatchHelper.Patch(harmony, runManagerType, "Abandon",
                postfix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(RunAbandonedPostfix)));

        PatchHelper.Log("LAN multiplayer patches applied.");
    }


    public static bool MessageToIdPrefix(INetMessage message, ref int __result)
    {
        if (!_isLanSessionActive)
            return true;
        if (message == null || !StableMessageTypeIds.TryGetValue(message.GetType(), out var typeId))
            return true;
        __result = typeId;
        return false;
    }

    public static bool TryGetMessageTypePrefix(int id, ref Type type, ref bool __result)
    {
        if (!_isLanSessionActive)
            return true;
        if (!StableMessageIdTypes.TryGetValue(id, out var stableType))
            return true;
        type = stableType;
        __result = true;
        return false;
    }

    public static bool TryDeserializeMessagePrefix(NetMessageBus __instance, byte[] packetBytes,
        ref INetMessage message, ref ulong? overrideSenderId, ref bool __result)
    {
        if (!_isLanSessionActive)
            return true;
        try
        {
            if (packetBytes == null || packetBytes.Length == 0 || !StableMessageIdTypes.ContainsKey(packetBytes[0]))
                return true;
            var reader = (PacketReader?)AccessTools.Field(typeof(NetMessageBus), "_reader")?.GetValue(__instance);
            if (reader == null) return true;
            reader.Reset(packetBytes);
            var typeId = reader.ReadByte();
            if (!StableMessageIdTypes.TryGetValue(typeId, out var type))
            {
                PatchHelper.Log($"LAN message decode: unknown id {typeId}, falling back");
                return true;
            }
            overrideSenderId = reader.ReadULong();
            var msg = (INetMessage?)Activator.CreateInstance(type);
            if (msg == null) return true;
            message = msg;
            message.Deserialize(reader);
            __result = true;
            return false;
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"LAN message decode failed: {exception}");
            __result = false;
            return false;
        }
    }

    public static bool JoinFriendOpenedPrefix(NJoinFriendScreen __instance)
    {
        if (!_isLanSessionActive)
        {
            CleanupLanJoinScreen(__instance);
            return true;
        }
        try
        {
            var loadingOverlay = __instance.GetNodeOrNull<Control>("%LoadingOverlay");
            if (loadingOverlay != null) loadingOverlay.Visible = false;
            ConfigureLanJoinScreen(__instance);
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"LAN join screen patch failed, falling back to original: {exception}");
            return true;
        }
        return false;
    }

    public static bool JoinFriendRefreshButtonPrefix(NJoinFriendScreen __instance)
    {
        if (!_isLanSessionActive)
            return true;
        if (!TryGetLanInputs(__instance, out var hostInput, out var portInput))
            return true;
        if (hostInput == null || portInput == null) return true;
        TaskHelper.RunSafely(JoinLanGameAsync(__instance, hostInput, portInput));
        return false;
    }

    public static void JoinFriendClosedPostfix(object __instance)
    {
        if (!_isLanSessionActive)
            return;
        CancelJoinFlow(__instance);
    }

    private static void CancelJoinFlow(object? screen)
    {
        try
        {
            if (screen == null) return;
            var currentJoinFlow = AccessTools.Field(screen.GetType(), "_currentJoinFlow")?.GetValue(screen)
                ?? AccessTools.Field(typeof(NJoinFriendScreen), "_currentJoinFlow")?.GetValue(screen);
            var cancelToken = currentJoinFlow?.GetType().GetProperty("CancelToken")?.GetValue(currentJoinFlow);
            cancelToken?.GetType().GetMethod("Cancel", Type.EmptyTypes)?.Invoke(cancelToken, null);
        }
        catch { }
    }

    public static bool HostSubmenuStartHostAsyncPrefix(GameMode gameMode, Control loadingOverlay, NSubmenuStack stack, ref Task __result)
    {
        if (!_isLanSessionActive)
            return true;
        try
        {
            __result = StartHostFromSubmenuAsync(gameMode, loadingOverlay, stack);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"HostSubmenuStartHostAsyncPrefix failed: {ex}");
            if (loadingOverlay != null) loadingOverlay.Visible = false;
            ShowInfoPopup(Strings.T("局域网主机错误", "LAN Host Error"), Strings.T("无法启动局域网主机。详情请查看游戏日志。", "Could not start the LAN host. See the game log for details."));
            __result = Task.CompletedTask;
        }
        return false;
    }

    public static bool MultiplayerSubmenuStartHostAsyncPrefix(object __instance, SerializableRun run, ref Task __result)
    {
        if (!_isLanSessionActive)
            return true;
        try
        {
            __result = StartLoadedHostAsync(__instance, run);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"MultiplayerSubmenuStartHostAsyncPrefix failed: {ex}");
            ShowInfoPopup(Strings.T("局域网主机错误", "LAN Host Error"), Strings.T("无法启动局域网主机。详情请查看游戏日志。", "Could not start the LAN host. See the game log for details."));
            __result = Task.CompletedTask;
        }
        return false;
    }

    public static void HostServiceDisconnectPostfix()
        {
            if (!_isLanSessionActive)
                return;
            // Clean up host resources (discovery, name sync, UI button)
            // but keep _isLanSessionActive true so the user can pick
            // another game mode from NMultiplayerHostSubmenu and host
            // again via LAN. The flag is cleared later by
            // MultiplayerSubmenuShownPostfix when the user navigates
            // all the way back to the top-level multiplayer menu.
            try { StopDiscoveryResponder(); } catch { }
            try { UnregisterNameSyncHandler(); } catch { }
            try { StopHostIpButton(); } catch { }
        }

    public static void RunAbandonedPostfix()
        {
            if (!_isLanSessionActive)
                return;
            // Run abandoned - clean up resources but keep the LAN flag
            // alive so the player can start another run from the host
            // submenu without re-selecting Host LAN.
            try { StopDiscoveryResponder(); } catch { }
            try { StopHostIpButton(); } catch { }
        }



    private static bool ShouldApplyLocalLanPatches()
    {
        if (!LanConfig.GetBool("lan_multiplayer_enabled", true))
        {
            PatchHelper.Log("LAN multiplayer patches disabled by config.");
            return false;
        }
        if (IsSts2GameLobbyModLoaded())
        {
            PatchHelper.Log("Detected STS2 Game Lobby / sts2_lan_connect mod; skipping to avoid protocol conflicts.");
            return false;
        }
        return true;
    }

    private static void PatchMultiplayerSaveCanonicalization(Harmony harmony)
    {
        try
        {
            var runSaveManagerType = typeof(SaveManager).Assembly.GetType("MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager", throwOnError: false);
            if (runSaveManagerType == null)
            {
                PatchHelper.Log("SKIPPED RunSaveManager.LoadAndCanonicalizeMultiplayerRunSave: type not found");
                return;
            }
            PatchHelper.Patch(harmony, runSaveManagerType, "LoadAndCanonicalizeMultiplayerRunSave",
                prefix: PatchHelper.Method(typeof(LanMultiplayerPatches), nameof(LoadAndCanonicalizeMultiplayerRunSavePrefix)));
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"FAILED RunSaveManager compatibility patch setup: {exception}");
        }
    }

    private static object? TryLoadRawMultiplayerSave(object? runSaveManager)
    {
        var method = runSaveManager?.GetType().GetMethod("LoadMultiplayerRunSave", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var result = method?.Invoke(runSaveManager, null);
        if (result == null) return null;
        var success = result.GetType().GetProperty("Success", BindingFlags.Public | BindingFlags.Instance)?.GetValue(result);
        if (success is bool ok && !ok) return null;
        return result.GetType().GetProperty("SaveData", BindingFlags.Public | BindingFlags.Instance)?.GetValue(result);
    }

    private static IEnumerable<ulong> GetSavePlayerIds(object? saveData)
    {
        var players = saveData?.GetType().GetProperty("Players", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saveData) as System.Collections.IEnumerable;
        if (players == null) yield break;
        foreach (var player in players)
        {
            if (player == null) continue;
            var value = player.GetType().GetProperty("NetId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(player);
            if (value is ulong playerId && playerId > 0)
                yield return playerId;
        }
    }

    private static bool TryResolveMultiplayerSavePlayerId(IReadOnlyCollection<ulong> savePlayerIds, out ulong playerId)
    {
        playerId = 0;
        if (savePlayerIds == null || savePlayerIds.Count == 0) return false;
        foreach (var candidate in GetStableMultiplayerSavePlayerIdCandidates())
        {
            if (candidate > 0 && savePlayerIds.Contains(candidate))
            {
                playerId = candidate;
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<ulong> GetStableMultiplayerSavePlayerIdCandidates()
    {
        if (TryGetStoredLanId(LanMultiplayerSavePlayerIdSettingKey, out var savedMultiplayerId))
            yield return savedMultiplayerId;
        if (TryGetStoredLanId(LanPlayerIdSettingKey, out var generatedLanId))
            yield return generatedLanId;
    }

    private static bool TryGetStoredLanId(string key, out ulong value)
    {
        value = 0;
        var raw = LanConfig.GetString(key, string.Empty);
        return ulong.TryParse(raw, out value) && value > 0;
    }

    private static void RememberMultiplayerSavePlayerId(ulong playerId)
    {
        if (playerId > 0)
            SaveLanSetting(LanMultiplayerSavePlayerIdSettingKey, playerId);
    }


    private static bool IsSts2GameLobbyModLoaded()
    {
        try
        {
            var getLoadedMods = typeof(ModManager).GetMethod("GetLoadedMods", BindingFlags.Public | BindingFlags.Static);
            if (getLoadedMods?.Invoke(null, null) is System.Collections.IEnumerable loadedMods)
            {
                foreach (var mod in loadedMods)
                    if (IsSts2GameLobbyMod(mod)) return true;
            }
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Failed to inspect loaded mods for STS2 Game Lobby: {exception}");
        }
        try
        {
            return AppDomain.CurrentDomain.GetAssemblies().Any(IsSts2GameLobbyAssembly);
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Failed to inspect loaded assemblies for STS2 Game Lobby: {exception}");
            return false;
        }
    }

    private static bool IsSts2GameLobbyMod(object mod)
    {
        if (mod == null) return false;
        var type = mod.GetType();
        var manifest = type.GetField("manifest", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mod)
            ?? type.GetProperty("manifest", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mod);
        if (manifest != null)
        {
            var manifestType = manifest.GetType();
            if (IsSts2GameLobbyIdentifier(manifestType.GetField("id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(manifest) as string)
                || IsSts2GameLobbyIdentifier(manifestType.GetProperty("id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(manifest) as string)
                || IsSts2GameLobbyIdentifier(manifestType.GetField("name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(manifest) as string)
                || IsSts2GameLobbyIdentifier(manifestType.GetProperty("name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(manifest) as string))
                return true;
        }
        var assembly = type.GetField("assembly", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mod) as Assembly
            ?? type.GetProperty("assembly", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mod) as Assembly;
        return assembly != null && IsSts2GameLobbyAssembly(assembly);
    }

    private static bool IsSts2GameLobbyAssembly(Assembly assembly)
    {
        if (assembly == null || assembly == typeof(LanMultiplayerPatches).Assembly) return false;
        var assemblyName = assembly.GetName().Name;
        if (assemblyName != null && IsSts2GameLobbyIdentifier(assemblyName)) return true;
        try
        {
            return assembly.GetTypes().Any(type => type.FullName != null && type.FullName.StartsWith("Sts2LanConnect.", StringComparison.Ordinal));
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Any(type => type?.FullName != null && type.FullName.StartsWith("Sts2LanConnect.", StringComparison.Ordinal));
        }
    }

    private static bool IsSts2GameLobbyIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().Replace('-', '_').Replace(' ', '_');
        return normalized.Equals("sts2_lan_connect", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("sts2_game_lobby", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("sts2_lobby_connect", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("sts2_lan_connect", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("sts2_game_lobby", StringComparison.OrdinalIgnoreCase);
    }


    private static void BuildMessageTypeMaps()
    {
        if (StableMessageTypeIds.Count > 0) return;
        var sts2Assembly = typeof(ActionEnqueuedMessage).Assembly;
        var lanAssembly = typeof(LanMultiplayerPatches).Assembly;
        for (var index = 0; index < StableMessageTypeOrder.Length; index++)
        {
            var typeName = StableMessageTypeOrder[index];
            var type = sts2Assembly.GetType(typeName) ?? lanAssembly.GetType(typeName);
            if (type == null)
            {
                PatchHelper.Log($"LAN message protocol type missing: {typeName}");
                continue;
            }
            StableMessageTypeIds[type] = index;
            StableMessageIdTypes[index] = type;
        }
        PatchHelper.Log($"Stable LAN message protocol mapped {StableMessageTypeIds.Count}/{MessageTypeCount} types.");
    }


    private static List<string>? GetMultiplayerCompatibilityModNameList(List<string>? baseModNames)
    {
        // When LAN is active, suppress all mod names to prevent cross-platform
        // mismatch. PC reports "LanSpire", Android reports "STS2AndroidPortCompat"
        // -- these are different names for the same protocol, so the game's mod
        // compatibility check fails. By returning null (empty), both sides report
        // "no gameplay-affecting mods" and the check always passes.
        //
        // If users have real gameplay mods they want to declare, they can set
        // lan_compatibility_mod_names in config -- but the base mod list (which
        // contains platform-specific names) is always suppressed during LAN.
        var result = new List<string>();
        foreach (var configured in GetConfiguredCompatibilityModNames())
        {
            if (!result.Contains(configured, StringComparer.Ordinal))
                result.Add(configured);
        }
        return result.Count == 0 ? null : result;
    }

    private static IEnumerable<string> GetConfiguredCompatibilityModNames()
    {
        if (!LanConfig.TryGet("lan_compatibility_mod_names", out var element) || element.ValueKind != JsonValueKind.Array)
            yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            var value = NormalizeText(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString());
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                yield return value;
        }
    }


    private static int GetConfiguredHostMaxPlayers()
    {
        if (!LanConfig.GetBool("max_multiplayer_enabled", true))
            return DefaultMaxPlayers;
        var configured = LanConfig.GetInt("max_multiplayer_players", DefaultMaxPlayers);
        return configured <= 0 ? DefaultMaxPlayers : configured;
    }

    private static int GetHostCapacityForPlatform(PlatformType platformType, int requestedMaxPlayers)
    {
        requestedMaxPlayers = requestedMaxPlayers <= 0 ? DefaultMaxPlayers : requestedMaxPlayers;
        return platformType == PlatformType.Steam ? Math.Min(requestedMaxPlayers, SteamMaxPlayers) : requestedMaxPlayers;
    }


    private static volatile bool _forceRegeneratePeerId;

    private static ulong GetOrCreateLocalPeerId()
    {
        if (_forceRegeneratePeerId)
        {
            _forceRegeneratePeerId = false;
            // If using custom ID, we can't auto-regenerate - just fall through
            // to normal custom ID path. The user will need to change it manually.
            if (!LanConfig.GetBool("lan_use_custom_player_id", false))
            {
                var regenerated = 0UL;
                while (regenerated <= 1)
                {
                    regenerated = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))) & 0x7FFFFFFFFFFFFFFFUL;
                }
                SaveLanSetting(LanPlayerIdSettingKey, regenerated);
                PatchHelper.Log($"Regenerated LAN peer ID after collision: {regenerated}");
                return regenerated;
            }
            PatchHelper.Log("NetId collision with custom player ID - cannot auto-regenerate, user must change manually.");
        }
        if (TryGetConfiguredCustomPeerId(out var customPeerId))
            return customPeerId;
        var current = LanConfig.GetString(LanPlayerIdSettingKey, string.Empty);
        if (ulong.TryParse(current, out var saved) && saved > 1)
            return saved;
        var generated = 0UL;
        while (generated <= 1)
        {
            generated = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))) & 0x7FFFFFFFFFFFFFFFUL;
        }
        SaveLanSetting(LanPlayerIdSettingKey, generated);
        PatchHelper.Log($"Generated persistent LAN peer ID {generated}");
        return generated;
    }

    private static bool TryGetConfiguredCustomPeerId(out ulong peerId)
    {
        peerId = 0;
        if (!LanConfig.GetBool("lan_use_custom_player_id", false)) return false;
        var normalized = NormalizeText(LanConfig.GetString("lan_custom_player_id", string.Empty));
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        if (ulong.TryParse(normalized, out peerId) && peerId > 1) return true;
        peerId = MapCustomPeerId(normalized);
        return true;
    }

    private static string GetConfiguredPlayerDisplayName()
    {
        return LanConfig.GetBool("lan_use_custom_player_id", false)
            ? NormalizeText(LanConfig.GetString("lan_custom_player_id", string.Empty))
            : string.Empty;
    }

    private static ulong MapCustomPeerId(string normalizedPeerId)
    {
        var bytes = Encoding.UTF8.GetBytes(normalizedPeerId);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        var value = BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, 8)) & 0x7FFFFFFFFFFFFFFFUL;
        return value <= 1 ? value + 2 : value;
    }


    private static void SaveJoinEndpoint(string host, ushort port)
    {
        SaveLanSetting("lan_join_host", host.Trim());
        SaveLanSetting("lan_join_port", port);
        // NOTE: AddToJoinHistory is NOT called here. History should only record
        // successful joins, not every join attempt. History is added after the
        // join task completes successfully (see JoinHost.cs).
    }

    private static string GetSavedJoinHost() => LanConfig.GetString("lan_join_host", string.Empty);

    private static ushort GetSavedJoinPort()
    {
        var port = LanConfig.GetInt("lan_join_port", DefaultPort);
        return port is > 0 and <= 65535 ? (ushort)port : DefaultPort;
    }


    private const int MaxHistoryEntries = 5;

    private static void AddToJoinHistory(string host, ushort port)
    {
        try
        {
            var entry = $"{host}:{port}";
            LanConfig.ModifyRaw(json =>
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
                var history = new List<string>();
                if (node["lan_join_history"] is System.Text.Json.Nodes.JsonArray existingArr)
                    history.AddRange(existingArr.Select(i => i?.GetValue<string>() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)));
                history.Remove(entry);
                history.Insert(0, entry);
                if (history.Count > MaxHistoryEntries)
                    history = history.Take(MaxHistoryEntries).ToList();
                var arr = new System.Text.Json.Nodes.JsonArray();
                foreach (var h in history)
                    arr.Add(h);
                node["lan_join_history"] = arr;
                return node.ToJsonString(_jsonOpts);
            });
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Failed to save join history: {exception}");
        }
    }

    private static IReadOnlyList<string> GetJoinHistory()
    {
        try
        {
            if (!LanConfig.TryGet("lan_join_history", out var element) || element.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            return element.EnumerateArray()
                .Where(i => i.ValueKind == JsonValueKind.String)
                .Select(i => i.GetString() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(MaxHistoryEntries)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static void ClearJoinHistory()
    {
        try
        {
            LanConfig.ModifyRaw(json =>
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
                node.Remove("lan_join_history");
                return node.ToJsonString(_jsonOpts);
            });
            PatchHelper.Log("Join history cleared.");
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Failed to clear join history: {exception}");
        }
    }

    private static void RemoveFromJoinHistory(string entry)
    {
        try
        {
            LanConfig.ModifyRaw(json =>
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
                var history = new List<string>();
                if (node["lan_join_history"] is System.Text.Json.Nodes.JsonArray existingArr)
                    history.AddRange(existingArr.Select(i => i?.GetValue<string>() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)));
                history.Remove(entry);
                var arr = new System.Text.Json.Nodes.JsonArray();
                foreach (var h in history)
                    arr.Add(h);
                if (arr.Count > 0)
                    node["lan_join_history"] = arr;
                else
                    node.Remove("lan_join_history");
                return node.ToJsonString(_jsonOpts);
            });
            PatchHelper.Log($"Removed '{entry}' from join history.");
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Failed to remove entry from join history: {exception}");
        }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private static void SaveLanSetting(string key, object value)
    {
        LanConfig.ModifyRaw(json =>
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
            var previousJson = node.ToJsonString();
            node[key] = value switch
            {
                string s => s,
                int i => i,
                ushort u => u,
                ulong ul => ul.ToString(),
                _ => value?.ToString(),
            };
            var nextJson = node.ToJsonString();
            return string.Equals(previousJson, nextJson, StringComparison.Ordinal) ? json : node.ToJsonString(_jsonOpts);
        });
    }

    private static readonly object _playerNameLock = new();
    private static void SetPlayerNameOverride(PlatformType platformType, ulong playerId, string playerName)
    {
        lock (_playerNameLock)
        {
            if (string.IsNullOrWhiteSpace(playerName))
            {
                if (PlayerNameOverrides.TryGetValue(platformType, out var values))
                    values.Remove(playerId);
                return;
            }
            if (!PlayerNameOverrides.TryGetValue(platformType, out var map))
            {
                map = new Dictionary<ulong, string>();
                PlayerNameOverrides[platformType] = map;
            }
            map[playerId] = playerName.Trim();
        }
    }

    private static bool TryGetPlayerNameOverride(PlatformType platformType, ulong playerId, out string? playerName)
    {
        lock (_playerNameLock)
        {
            playerName = null;
            return PlayerNameOverrides.TryGetValue(platformType, out var map) && map.TryGetValue(playerId, out playerName) && !string.IsNullOrWhiteSpace(playerName);
        }
    }

    private static void ClearPlayerNameOverrides(PlatformType platformType)
    {
        lock (_playerNameLock)
            PlayerNameOverrides.Remove(platformType);
        SetPlayerNameOverride(platformType, GetOrCreateLocalPeerId(), GetConfiguredPlayerDisplayName());
    }

}