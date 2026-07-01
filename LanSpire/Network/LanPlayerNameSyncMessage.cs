using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace LanSpire.Network;

/// <summary>
/// Sent by client to host after connecting. Host replies with
/// LanPlayerNameSyncMessage containing the requesting player's name.
/// Also broadcast by host to all peers when a new player connects so
/// everyone has the full name map.
/// </summary>
public record struct LanPlayerNameSyncMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public bool ShouldBuffer => false;

    /// <summary>NetId of the player whose name is being synced.</summary>
    public ulong netId;

    /// <summary>Display name for that player.</summary>
    public string name;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(netId);
        writer.WriteString(name ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        netId = reader.ReadULong();
        name = reader.ReadString();
    }
}
