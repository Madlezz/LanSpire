namespace LanSpire.Helpers;

/// <summary>
/// Centralized names of game fields/methods accessed via reflection.
/// When the game updates and renames internals, you only need to update
/// this file instead of hunting through every .cs file.
/// </summary>
internal static class GameInternals
{
    // NMultiplayerSubmenu
    public const string Submenu_HostButton = "_hostButton";
    public const string SubmenuStack_PushSubmenuType = "PushSubmenuType";

    // NSubmenu
    public const string Submenu_Stack = "_stack";

    // NJoinFriendScreen
    public const string JoinScreen_JoinGameAsync = "JoinGameAsync";
    public const string JoinScreen_NetService = "NetService";

    // CombatStateSynchronizer
    public const string CombatSync_SyncCompletionSource = "_syncCompletionSource";

    // CombatManager
    public const string CombatManager_State = "_state";

    // TimingFailsafes target type (CombatStateSynchronizer or similar)
    public const string Sync_Synchronizer = "_synchronizer";
    public const string Sync_GetCursor = "GetCursor";
    public const string Sync_AddCursor = "AddCursor";
}
