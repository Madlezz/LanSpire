using System.Diagnostics.CodeAnalysis;

using LanSpire.Services;
using LanSpire.Patches;
namespace LanSpire;

/// <summary>
/// STS2 mod entry point. The game's ModManager finds this via
/// [ModInitializer] attribute and calls Initialize().
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModInitializer
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void Initialize()
    {
        PatchHelper.Log("LanSpire loading...");
        var harmony = new Harmony("lan.spire");

        LanConfig.TryMigrateFromOldFolder();

        LanConfig.EnsureDefaults();

        LanMultiplayerBootstrapPatches.Apply(harmony);

        SyncStallRecovery.Apply(harmony);
        SyncStallRecovery.ApplyPlayerChoiceTimeout(harmony);
        TimingFailsafes.Apply(harmony);
        DisconnectPrevention.Apply(harmony);

        System.AppDomain.CurrentDomain.ProcessExit += (_, _) => LanMultiplayerPatches.StopDiscoveryResponder();

        // Check for updates (fire-and-forget, non-blocking, once per session)
        _ = UpdateChecker.CheckAsync();

        PatchHelper.Log("LanSpire initialized.");
    }
}