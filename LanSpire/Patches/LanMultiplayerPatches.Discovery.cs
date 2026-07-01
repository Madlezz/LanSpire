namespace LanSpire.Patches;

public static partial class LanMultiplayerPatches
{


    /// <summary>Primary and fallback ports used for LAN discovery broadcasts.
    /// Multiple ports allow the host responder to bind even if 33772 is
    /// already occupied by another application on the same machine.</summary>
    private static readonly ushort[] DiscoveryPorts = [33772, 33773, 33774, 33775];
    private static CancellationTokenSource? _discoveryCts;
    private static Task? _discoveryTask;
    private static UdpClient? _discoveryUdp;
    private static volatile NetHostGameService? _hostService;
    private static bool _discoveryBindOk;
    private static ushort _boundDiscoveryPort;
    private static Godot.Timer? _playerCountTimer;
    private static int _lastPlayerCount = -1;

    private static void StartDiscoveryResponder(ushort gamePort)
    {
        StopDiscoveryResponder();
        _discoveryBindOk = false;
        _boundDiscoveryPort = 0;
        var cts = new CancellationTokenSource();
        _discoveryCts = cts;
        var token = cts.Token;
        var hostName = GetConfiguredPlayerDisplayName();
        if (string.IsNullOrWhiteSpace(hostName)) hostName = "Host";

        UdpClient? udp = null;
        foreach (var port in DiscoveryPorts)
        {
            try
            {
                udp = new UdpClient(port);
                _boundDiscoveryPort = port;
                _discoveryBindOk = true;
                break;
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"Discovery responder: failed to bind port {port}: {ex.Message}");
                udp = null;
            }
        }

        if (udp == null)
        {
            PatchHelper.Log($"Discovery responder: failed to bind any discovery port from {string.Join(",", DiscoveryPorts)}.");
            ShowToast(Strings.T(
                "局域网发现端口被占用，其他玩家可能无法自动发现本机。仍可手动输入 IP 加入。",
                "LAN discovery ports are in use. Auto-discovery may fail, but direct IP join still works."),
                5.0f);
            _discoveryCts = null;
            try { cts.Dispose(); } catch { }
            return;
        }

        _discoveryTask = Task.Run(async () =>
        {
            try
            {
                _discoveryUdp = udp;
                PatchHelper.Log($"LAN discovery responder started on port {_boundDiscoveryPort} (host: {hostName}).");
                while (!token.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try
                    {
                        result = await udp.ReceiveAsync();
                    }
                    catch (Exception) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    var text = Encoding.UTF8.GetString(result.Buffer);
                    if (text == "STS2_LAN_PING")
                    {
                        var peerCount = GetHostConnectedPeerCount();
                        if (peerCount < 0) peerCount = 0;
                        var total = peerCount + 1;
                        var capacity = GetConfiguredHostMaxPlayers();
                        var pong = Encoding.UTF8.GetBytes($"STS2_LAN_PONG:{ModVersion}:{gamePort}:{hostName}:{total}/{capacity}");
                        try { await udp.SendAsync(pong, pong.Length, result.RemoteEndPoint); } catch { }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    PatchHelper.Log($"Discovery responder error: {ex}");
            }
            finally
            {
                try { udp?.Close(); } catch { }
                _discoveryUdp = null;
                if (!token.IsCancellationRequested)
                {
                    if (ReferenceEquals(_discoveryCts, cts)) _discoveryCts = null;
                    _discoveryTask = null;
                    try { cts.Dispose(); } catch { }
                }
                else
                    PatchHelper.Log("LAN discovery responder stopped.");
            }
        }, token);
    }

    public static void StopDiscoveryResponder()
    {
        StopPlayerCountPoller();
        StopPingPoller();
        _hostService = null;
        try
        {
            var cts = _discoveryCts;
            _discoveryCts = null;
            if (cts == null) return;
            var udp = _discoveryUdp;
            _discoveryUdp = null;
            try { cts.Cancel(); } catch { }
            try { udp?.Close(); } catch { }
            var task = _discoveryTask;
            _discoveryTask = null;
            if (task != null)
            {
                try { task.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
            }
            try { cts.Dispose(); } catch { }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Discovery responder stop failed: {ex}");
        }
    }

    private static void StartPlayerCountPoller()
    {
        StopPlayerCountPoller();
        _lastPlayerCount = -1;
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null) return;

            // No floating label -- game's own HUD shows player status.
            // We only keep the poller for toast notifications (join/leave).
            var timer = new Godot.Timer
            {
                Name = "LanPlayerCountTimer",
                WaitTime = 1.0,
                Autostart = true,
                OneShot = false,
            };
            timer.Connect(Godot.Timer.SignalName.Timeout, Godot.Callable.From(OnPlayerCountTick));
            tree.Root.AddChild(timer);
            _playerCountTimer = timer;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Player count poller start failed: {ex}");
        }
    }

    private static void OnPlayerCountTick()
    {
        try
        {
            var count = GetHostConnectedPeerCount();
            if (count < 0)
            {
                if (_hostService != null)
                {
                    PatchHelper.Log("Host service no longer accessible - stopping player count poller and discovery responder.");
                    StopDiscoveryResponder();
                }
                return;
            }

            var inLobby = IsInLobbyPhase();
            if (!inLobby) return;

            if (_lastPlayerCount < 0)
            {
                _lastPlayerCount = count;
                return;
            }
            if (count == _lastPlayerCount) return;
            var capacity = GetConfiguredHostMaxPlayers();
            var total = count + 1;
            var delta = count - _lastPlayerCount;
            _lastPlayerCount = count;
            if (delta > 0)
                ShowToast(Strings.T($"有玩家加入（当前 {total}/{capacity} 人）", $"Player joined ({total}/{capacity} players)"));
            else
                ShowToast(Strings.T($"有玩家离开（当前 {total}/{capacity} 人）", $"Player left ({total}/{capacity} players)"));
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Player count poll: {ex}");
        }
    }

    private static void StopPlayerCountPoller()
    {
        var timer = _playerCountTimer;
        _playerCountTimer = null;
        if (timer != null && Godot.GodotObject.IsInstanceValid(timer))
        {
            try { timer.QueueFree(); } catch { }
        }
    }

    /// <summary>
    /// Returns true while the main menu / multiplayer submenu is still on
    /// screen (lobby phase). Once gameplay starts the main menu scene is
    /// swapped out, so we hide the floating player-count indicator.
    /// </summary>
    private static bool IsInLobbyPhase()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null) return true;
            return FindNodeOfType(tree.Root, typeof(NMainMenu)) != null;
        }
        catch { return true; }
    }

    private static Node? FindNodeOfType(Node? root, Type type)
    {
        if (root == null) return null;
        if (type.IsInstanceOfType(root)) return root;
        foreach (var child in root.GetChildren())
        {
            if (child is Node childNode)
            {
                var found = FindNodeOfType(childNode, type);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static int GetHostConnectedPeerCount()
    {
        try
        {
            var svc = _hostService;
            if (svc == null) return -1;
            var peers = svc.GetType().GetProperty("ConnectedPeers")?.GetValue(svc);
            if (peers == null)
            {
                if (!_loggedPeerPropMissing)
                {
                    _loggedPeerPropMissing = true;
                    var propNames = string.Join(", ", svc.GetType().GetProperties().Select(p => p.Name));
                    PatchHelper.Log($"ConnectedPeers not found on {svc.GetType().Name}. Available: {propNames}");
                }
                return 0;
            }
            if (peers is System.Collections.ICollection col) return col.Count;
            var countProp = peers.GetType().GetProperty("Count");
            if (countProp?.GetValue(peers) is int c) return c;
            return -1;
        }
        catch { return -1; }
    }

    private static bool _loggedPeerPropMissing;

    private static async Task<List<(string ip, ushort port, string name, string? count)>> DiscoverLanGamesAsync(int timeoutMs = 3000)
    {
        var games = new List<(string ip, ushort port, string name, string? count)>();
        try
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            var ping = Encoding.UTF8.GetBytes("STS2_LAN_PING");

            foreach (var discPort in DiscoveryPorts)
            {
                try { await udp.SendAsync(ping, ping.Length, new IPEndPoint(IPAddress.Broadcast, discPort)); }
                catch { }
                foreach (var broadcastAddr in GetSubnetBroadcastAddresses())
                {
                    try { await udp.SendAsync(ping, ping.Length, new IPEndPoint(broadcastAddr, discPort)); }
                    catch { }
                }
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) break;
                var receiveTask = udp.ReceiveAsync();
                if (await Task.WhenAny(receiveTask, Task.Delay(remaining)) != receiveTask)
                {
                    _ = receiveTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    break;
                }
                var result = await receiveTask;
                var text = Encoding.UTF8.GetString(result.Buffer);
                if (text.StartsWith("STS2_LAN_PONG:", StringComparison.Ordinal))
                {
                    // Format: STS2_LAN_PONG:<version>:<port>:<hostName>:<count>
                    // Old hosts without version tag: STS2_LAN_PONG:<port>:<hostName>:<count>
                    // We detect the new format by checking if the first field after the
                    // prefix starts with 'v' (version tag like "v0.7.0").
                    var afterPrefix = text["STS2_LAN_PONG:".Length..];
                    var fields = afterPrefix.Split(':');
                    string? hostVersion = null;
                    int portIdx = 0;
                    // If first field looks like a version (starts with 'v' + digit),
                    // shift everything by one.
                    if (fields.Length > 0 && fields[0].Length > 1 && fields[0][0] == 'v' && char.IsDigit(fields[0][1]))
                    {
                        hostVersion = fields[0];
                        portIdx = 1;
                    }
                    if (fields.Length > portIdx && ushort.TryParse(fields[portIdx], out var port))
                    {
                        var ip = result.RemoteEndPoint.Address.ToString();
                        // Everything between port field and last field is the name
                        // (names can contain colons in theory, so we rejoin).
                        var nameStart = portIdx + 1;
                        var nameEnd = fields.Length - 1;
                        string name = nameStart < nameEnd
                            ? string.Join(":", fields, nameStart, nameEnd - nameStart)
                            : (nameStart < fields.Length ? fields[nameStart] : "");
                        string? count = null;
                        // Last field may be "N/C" count
                        if (fields.Length > nameEnd)
                        {
                            var afterColon = fields[nameEnd];
                            var slashIdx = afterColon.IndexOf('/');
                            if (slashIdx > 0 && int.TryParse(afterColon.AsSpan(0, slashIdx), out _))
                            {
                                count = afterColon;
                                if (nameEnd > nameStart)
                                    name = string.Join(":", fields, nameStart, nameEnd - nameStart);
                            }
                        }
                        // Version mismatch warning
                        if (hostVersion != null && hostVersion != ModVersion)
                        {
                            PatchHelper.Log($"LAN discovery: host at {ip}:{port} is version {hostVersion}, you are {ModVersion}. Possible incompatibility.");
                        }
                        if (!games.Any(g => g.ip == ip && g.port == port))
                            games.Add((ip, port, name, count));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"LAN discovery scan: {ex}");
        }
        return games;
    }

}