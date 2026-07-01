using LanSpire.Network;
namespace LanSpire.Patches;

public static partial class LanMultiplayerPatches
{

    private static async Task JoinLanGameAsync(NJoinFriendScreen screen, LineEdit hostInput, LineEdit portInput)
    {
        if (_isJoining)
        {
            PatchHelper.Log("Join already in progress - ignoring duplicate request.");
            return;
        }
        _isJoining = true;
        Control? loadingOverlay = null;
        Godot.Button? joinBtn = null;
        Label? statusLabel = null;
        try
        {
            if (!TryParseEndpoint(hostInput.Text, portInput.Text, out var host, out var port, out var error))
            {
                ShowInfoPopup(Strings.T("无法加入局域网房间", "Unable to join LAN game"), error);
                return;
            }
            hostInput.Text = host;
            portInput.Text = port.ToString();
            SaveJoinEndpoint(host, port);

            loadingOverlay = screen.GetNodeOrNull<Control>("LanLoadingOverlay");
            if (loadingOverlay != null)
            {
                loadingOverlay.Visible = true;
                statusLabel = loadingOverlay.GetNodeOrNull<Label>("StatusLabel");
                if (statusLabel != null)
                    statusLabel.Text = Strings.T($"正在连接 {host}:{port}...", $"Connecting to {host}:{port}...");
            }

            var refreshNode = screen.GetNodeOrNull<Control>("RefreshButton");
            if (refreshNode is Godot.Button b) { joinBtn = b; joinBtn.Disabled = true; }
            hostInput.Editable = false;
            portInput.Editable = false;
            var scanBtnNode = screen.GetNodeOrNull<Godot.BaseButton>("Panel/LanJoinContainer/LanScanRow/LanScanBtn");
            if (scanBtnNode != null) scanBtnNode.Disabled = true;

            var method = typeof(NJoinFriendScreen).GetMethod(GameInternals.JoinScreen_JoinGameAsync, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                PatchHelper.Log("JoinGameAsync method not found - cannot join LAN game");
                ShowInfoPopup(Strings.T("加入失败", "Join Failed"), "JoinGameAsync method not found.");
                return;
            }

            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    if (statusLabel != null)
                        Callable.From(() =>
                        {
                            if (Godot.GodotObject.IsInstanceValid(statusLabel))
                                statusLabel.Text = Strings.T("重试中...", "Retrying...");
                        }).CallDeferred();
                    await Task.Delay(1500);
                }

                try
                {
                    var initializer = new ENetClientConnectionInitializer(GetOrCreateLocalPeerId(), host, port);
                    var joinTask = (Task?)method.Invoke(screen, new object[] { initializer });
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
                    if (joinTask == null || await Task.WhenAny(joinTask, timeoutTask) != timeoutTask)
                    {
                        // Join succeeded - register name sync and send our name
                        try
                        {
                            var netServiceProp = typeof(NJoinFriendScreen).GetProperty(GameInternals.JoinScreen_NetService,
                                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                            var netService = netServiceProp?.GetValue(screen) as INetGameService;
                            if (netService != null && netService.IsConnected)
                            {
                                RegisterNameSyncHandler(netService);
                                SendLocalPlayerName();
                                SendPassphrase();
                                StartPingPoller();
                            }
                        }
                        catch (Exception nameSyncEx)
                        {
                            PatchHelper.Log($"Name sync setup after join failed: {nameSyncEx}");
                        }
                        // Only record history on successful join, not on every attempt
                        AddToJoinHistory(host, port);
                        return;
                    }

                    // Timeout won - observe the orphaned task to prevent
                    // unobserved exception and potential ENet resource leak
                    _ = joinTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            PatchHelper.Log($"Orphaned LAN join task failed after timeout: {t.Exception?.InnerException?.Message ?? t.Exception?.Message}");
                    }, TaskContinuationOptions.ExecuteSynchronously);

                    PatchHelper.Log($"LAN join attempt {attempt}/{maxAttempts} to {host}:{port} timed out.");
                    CancelJoinFlow(screen);
                    if (attempt < maxAttempts)
                        continue;
                    Callable.From(() => ShowInfoPopup(Strings.T("加入失败", "Join Failed"),
                        Strings.T("连接超时。请确认房主在线、IP 正确、且防火墙已放行端口。", "Connection timed out. Make sure the host is online, the IP is correct, and the port is allowed through firewall."))).CallDeferred();
                    return;
                }
                catch (Exception ex)
                {
                    var real = ex;
                    while (real is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
                        real = tie.InnerException;

                    if (real is OperationCanceledException or TaskCanceledException)
                    {
                        PatchHelper.Log("LAN join cancelled.");
                        return;
                    }

                    if (IsTransientJoinError(real) && attempt < maxAttempts)
                    {
                        PatchHelper.Log($"LAN join attempt {attempt} failed ({real.GetType().Name}: {real}), retrying...");
                        CancelJoinFlow(screen);
                        continue;
                    }

                    // Detect NetId collision: base game sends IdCollision -> NetError.Kicked.
                    // Regenerate peer ID and retry with the new ID.
                    if (IsNetIdCollision(real) && attempt < maxAttempts)
                    {
                        PatchHelper.Log($"LAN join attempt {attempt} failed - NetId collision detected, regenerating peer ID and retrying.");
                        _forceRegeneratePeerId = true;
                        CancelJoinFlow(screen);
                        continue;
                    }

                    PatchHelper.Log($"LAN join failed: {real}");
                    var friendlyMsg = TranslateJoinError(real);
                    Callable.From(() => ShowInfoPopup(Strings.T("加入失败", "Join Failed"), friendlyMsg)).CallDeferred();
                    return;
                }
            }
        }
        finally
        {
            var overlay = loadingOverlay;
            var btn = joinBtn;
            var hostEdit = hostInput;
            var portEdit = portInput;
            _isJoining = false;
            Callable.From(() =>
            {
                // Another join started - don't undo its UI state
                if (_isJoining) return;
                if (overlay != null && Godot.GodotObject.IsInstanceValid(overlay)) overlay.Visible = false;
                if (btn != null && Godot.GodotObject.IsInstanceValid(btn)) btn.Disabled = false;
                if (hostEdit != null && Godot.GodotObject.IsInstanceValid(hostEdit)) hostEdit.Editable = true;
                if (portEdit != null && Godot.GodotObject.IsInstanceValid(portEdit)) portEdit.Editable = true;
                var sb = screen.GetNodeOrNull<Godot.BaseButton>("Panel/LanJoinContainer/LanScanRow/LanScanBtn");
                if (sb != null && Godot.GodotObject.IsInstanceValid(sb)) sb.Disabled = false;
            }).CallDeferred();
        }
    }

    private static async Task ScanLanAsync(NJoinFriendScreen screen, Button scanBtn, ScrollContainer scanScroll, LineEdit hostInput, LineEdit portInput)
    {
        if (_isScanning)
        {
            PatchHelper.Log("LAN scan already in progress - ignoring duplicate request.");
            return;
        }
        _isScanning = true;
        var resultsContainer = scanScroll.GetNodeOrNull<VBoxContainer>("LanScanResultsInner");
        if (resultsContainer == null)
        {
            PatchHelper.Log("ScanLanAsync: inner results container missing");
            _isScanning = false;
            return;
        }

        if (!Godot.GodotObject.IsInstanceValid(scanBtn) || !Godot.GodotObject.IsInstanceValid(scanScroll) || !Godot.GodotObject.IsInstanceValid(resultsContainer))
        {
            _isScanning = false;
            return;
        }
        scanBtn.Disabled = true;
        scanBtn.Text = Strings.T("扫描中...", "Scanning...");

        foreach (var child in resultsContainer.GetChildren())
            child.QueueFree();
        scanScroll.Visible = true;

        var statusLabel = MakeLabel("ScanStatus", Strings.T("正在搜索局域网内的游戏...", "Searching for games on LAN..."), 16, new Color(0.6f, 0.6f, 0.6f));
        resultsContainer.AddChild(statusLabel);

        List<(string ip, ushort port, string name, string? count)> games;
        try
        {
            games = await DiscoverLanGamesAsync(3000);
        }
        catch (Exception ex)
        {
            ReportError("LAN scan", ex);
            games = new List<(string, ushort, string, string?)>();
        }

        Callable.From(() =>
        {
            try
            {
                if (!Godot.GodotObject.IsInstanceValid(resultsContainer))
                    return;
                if (Godot.GodotObject.IsInstanceValid(statusLabel))
                    statusLabel.QueueFree();

                if (games.Count == 0)
                {
                    var noResults = MakeLabel("ScanEmpty",
                        Strings.T($"未找到游戏。请确认房主已开始游戏，且防火墙已放行 UDP 端口 {DefaultPort}。", $"No games found. Make sure the host has started a game and UDP port {DefaultPort} is allowed through firewall."),
                        16, new Color(0.6f, 0.6f, 0.6f));
                    noResults.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    noResults.CustomMinimumSize = new Vector2(400, 0);
                    resultsContainer.AddChild(noResults);
                }
                else
                {
                    var foundLabel = MakeLabel("ScanFound",
                        Strings.T($"找到 {games.Count} 个游戏：", $"{games.Count} game(s) found:"),
                        16, new Color(0.937f, 0.784f, 0.318f));
                    resultsContainer.AddChild(foundLabel);

                    foreach (var game in games)
                    {
                        var displayText = string.IsNullOrWhiteSpace(game.name)
                            ? $"{game.ip}:{game.port}"
                            : $"{game.name} ({game.ip}:{game.port})";
                        var isFull = false;
                        if (!string.IsNullOrEmpty(game.count))
                        {
                            var slashIdx = game.count.IndexOf('/');
                            if (slashIdx > 0 && int.TryParse(game.count.AsSpan(0, slashIdx), out var current) &&
                                int.TryParse(game.count.AsSpan(slashIdx + 1), out var cap) && current >= cap)
                            {
                                isFull = true;
                                displayText += " [" + Strings.T("已满", "Full") + "]";
                            }
                            else
                                displayText += $" [{game.count}]";
                        }
                        var btn = new Button
                        {
                            Text = displayText,
                            CustomMinimumSize = new Vector2(0, 40),
                            Disabled = isFull,
                        };
                        btn.AddThemeFontSizeOverride("font_size", 16);
                        StyleLanButton(btn);
                        if (isFull)
                            btn.Modulate = new Color(0.6f, 0.6f, 0.6f);
                        var ip = game.ip;
                        var port = game.port;
                        btn.Connect(Button.SignalName.Pressed, Callable.From(() =>
                        {
                            hostInput.Text = ip;
                            portInput.Text = port.ToString();
                            TaskHelper.RunSafely(JoinLanGameAsync(screen, hostInput, portInput));
                        }));
                        resultsContainer.AddChild(btn);
                    }
                }
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"Scan UI update failed: {ex}");
            }
            finally
            {
                _isScanning = false;
                if (Godot.GodotObject.IsInstanceValid(scanBtn))
                {
                    scanBtn.Disabled = false;
                    scanBtn.Text = Strings.T("扫描局域网", "Scan LAN");
                }
            }
        }).CallDeferred();
    }

}