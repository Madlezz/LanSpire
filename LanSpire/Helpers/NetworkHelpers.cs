using LanSpire.Patches;

namespace LanSpire.Helpers;

/// <summary>Pure network utility functions: IP enumeration, subnet broadcast,
/// port parsing, port-in-use check. No shared state.</summary>
internal static class NetworkHelpers
{
    /// <summary>An IPv4 address with its adapter metadata.</summary>
    internal sealed record NetAddress(string Ip, string AdapterName, string Label, int Priority)
    {
        /// <summary>True if this address is on a known VPN adapter.</summary>
        internal bool IsVpn => Priority >= 10;
    }

    internal static string NormalizeText(string raw) => (raw ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);

    internal static bool TryParseEndpoint(string hostInput, string portInput, out string host, out ushort port, out string error)
    {
        host = (hostInput ?? string.Empty).Trim();
        var portText = (portInput ?? string.Empty).Trim();
        if (TryExtractInlinePort(ref host, out var inlinePort))
            portText = inlinePort.ToString();
        if (string.IsNullOrWhiteSpace(host))
        {
            port = 0;
            error = Strings.T("请输入房主 IP 地址。", "Enter the host IP address.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(portText))
        {
            port = LanMultiplayerPatches.DefaultPort;
            error = string.Empty;
            return true;
        }
        if (!ushort.TryParse(portText, out port) || port == 0)
        {
            error = Strings.T("端口必须是 1 到 65535 之间的数字。", "Port must be a number from 1 to 65535.");
            return false;
        }
        error = string.Empty;
        return true;
    }

    internal static bool TryExtractInlinePort(ref string host, out ushort port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(host)) return false;
        var colon = Math.Max(host.LastIndexOf(':'), host.LastIndexOf('：'));
        if (colon <= 0) return false;
        var hostPart = host[..colon].Trim();
        var portPart = host[(colon + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(hostPart) || string.IsNullOrWhiteSpace(portPart) || hostPart.Contains(':') || hostPart.Contains('：'))
            return false;
        if (!ushort.TryParse(portPart, out port) || port == 0) return false;
        host = hostPart;
        return true;
    }

    /// <summary>Enumerates local IPv4 addresses with adapter metadata.
    /// VPN adapters (ZeroTier, Tailscale, WireGuard, OpenVPN, Hamachi, Radmin)
    /// are detected by name and flagged so the host popup can label them
    /// correctly and prioritise the VPN IP for clipboard copy.</summary>
    internal static IReadOnlyList<NetAddress> GetNetworkAddresses()
    {
        var result = new List<NetAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var (vpnLabel, priority) = ClassifyAdapter(nic.Name, nic.Description);
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;
                    var ipText = addr.Address.ToString();
                    if (ipText.StartsWith("169.254.", StringComparison.Ordinal)) continue;

                    var effectivePriority = priority;
                    if (vpnLabel == null)
                    {
                        if (ipText.StartsWith("192.168.", StringComparison.Ordinal) ||
                            IsPrivate172(ipText))
                            effectivePriority = 5;
                        else
                            effectivePriority = 1;
                    }

                    result.Add(new NetAddress(ipText, nic.Name, vpnLabel ?? "", effectivePriority));
                }
            }
        }
        catch (Exception exception)
        {
            PatchHelper.Log($"Failed to enumerate network addresses: {exception}");
        }
        return result.OrderByDescending(a => a.Priority)
                      .ThenBy(a => a.Ip, StringComparer.Ordinal)
                      .ToList();
    }

    /// <summary>Back-compat: returns IP strings only, ordered by priority.</summary>
    internal static IEnumerable<string> GetLocalIpv4Addresses()
        => GetNetworkAddresses().Select(a => a.Ip);

    /// <summary>Detects known VPN adapters by name/description.
    /// Returns (label, priority) where label is null for non-VPN.</summary>
    private static (string? label, int priority) ClassifyAdapter(string name, string description)
    {
        var combined = $"{name} {description}";
        if (combined.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase))
            return ("ZeroTier", 20);
        if (combined.Contains("Tailscale", StringComparison.OrdinalIgnoreCase))
            return ("Tailscale", 20);
        if (combined.Contains("WireGuard", StringComparison.OrdinalIgnoreCase))
            return ("WireGuard", 18);
        if (combined.Contains("Hamachi", StringComparison.OrdinalIgnoreCase))
            return ("Hamachi", 15);
        if (combined.Contains("Radmin", StringComparison.OrdinalIgnoreCase))
            return ("Radmin", 15);
        if (combined.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("TAP NordVPN", StringComparison.OrdinalIgnoreCase))
            return ("OpenVPN", 15);
        return (null, 0);
    }

    /// <summary>Returns true if the IP is in the 172.16.0.0 - 172.31.255.255
    /// private range (RFC 1918). 172.0.0.0/12 = 172.16.0.0 - 172.31.255.255.</summary>
    private static bool IsPrivate172(string ipText)
    {
        if (!ipText.StartsWith("172.", StringComparison.Ordinal))
            return false;
        var parts = ipText.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var b))
            return false;
        return b is >= 16 and <= 31;
    }

    internal static IEnumerable<IPAddress> GetSubnetBroadcastAddresses()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;
                    var mask = addr.IPv4Mask;
                    if (mask == null || mask.Equals(IPAddress.Any)) continue;
                    var addrBytes = addr.Address.GetAddressBytes();
                    var maskBytes = mask.GetAddressBytes();
                    var broadcastBytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                        broadcastBytes[i] = (byte)(addrBytes[i] | ~maskBytes[i]);
                    result.Add(new IPAddress(broadcastBytes));
                }
            }
        }
        catch { }
        return result;
    }

    internal static bool IsPortInUse(ushort port)
    {
        try
        {
            var activeUdpConnections = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveUdpListeners();
            return activeUdpConnections.Any(l => l.Port == port);
        }
        catch { return false; }
    }
}