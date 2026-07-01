using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace LanSpire.Network;

/// <summary>
/// Ping/pong message for latency measurement. Client sends to host with
/// timestampMs. Host echoes back (broadcast). Client matches senderId
/// and calculates RTT = now - timestampMs.
/// </summary>
public record struct LanPingMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Unreliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => false;

    /// <summary>Unix epoch milliseconds when the ping was sent.</summary>
    public ulong timestampMs;

    /// <summary>NetId of the original sender (for echo matching).</summary>
    public ulong senderId;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(timestampMs);
        writer.WriteULong(senderId);
    }

    public void Deserialize(PacketReader reader)
    {
        timestampMs = reader.ReadULong();
        senderId = reader.ReadULong();
    }
}
