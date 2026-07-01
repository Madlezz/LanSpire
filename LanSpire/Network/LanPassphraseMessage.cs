using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace LanSpire.Network;

/// <summary>
/// Sent by client to host after connecting. Host validates the passphrase
/// hash against its configured lan_host_passphrase. If mismatch or timeout,
/// host disconnects the peer.
/// </summary>
public record struct LanPassphraseMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => false;

    /// <summary>SHA256 hash of the passphrase (hex string, lowercase).</summary>
    public string hash;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(hash ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        hash = reader.ReadString();
    }
}
