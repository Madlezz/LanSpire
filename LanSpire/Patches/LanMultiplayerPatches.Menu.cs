namespace LanSpire.Patches;

public static partial class LanMultiplayerPatches
{

    public static void UpdateButtonsPostfix(NMultiplayerSubmenu __instance)
    {
        try
        {
            var lanHost = __instance.FindChild("LanHostButton", true, false);
            var lanJoin = __instance.FindChild("LanJoinButton", true, false);
            if (lanHost == null) return;

            var hostBtnField = typeof(NMultiplayerSubmenu).GetField(GameInternals.Submenu_HostButton,
                BindingFlags.NonPublic | BindingFlags.Instance);
            var nativeHost = hostBtnField?.GetValue(__instance) as CanvasItem;

            if (nativeHost != null && lanHost is CanvasItem hostCi)
                hostCi.Visible = nativeHost.Visible;

            if (lanJoin is CanvasItem joinCi)
                joinCi.Visible = true;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"UpdateButtons postfix failed: {ex}");
        }
    }

    public static void MultiplayerSubmenuShownPostfix(NMultiplayerSubmenu __instance)
    {
        if (!IsLanSessionInFlight())
            _isLanSessionActive = false;

        try
        {
            if (__instance.FindChild("LanHostButton", true, false) != null)
            {
                PatchHelper.Log("LAN: buttons already present, skipping injection.");
                return;
            }

            PatchHelper.Log("LAN: injecting Host LAN / Join LAN buttons...");

            var hostBtnField = typeof(NMultiplayerSubmenu).GetField(GameInternals.Submenu_HostButton,
                BindingFlags.NonPublic | BindingFlags.Instance);
            var hostBtn = hostBtnField?.GetValue(__instance) as NSubmenuButton;
            if (hostBtn == null) { PatchHelper.Log("LAN: _hostButton not found"); return; }

            var parent = hostBtn.GetParent();
            if (parent == null) { PatchHelper.Log("LAN: button parent not found"); return; }

            var lanHostBtn = CreateLanSubmenuTile(hostBtn, "LanHostButton");
            var lanJoinBtn = CreateLanSubmenuTile(hostBtn, "LanJoinButton");
            if (lanHostBtn == null || lanJoinBtn == null)
            {
                PatchHelper.Log("LAN: tile instantiation failed - aborting injection.");
                return;
            }

            lanHostBtn.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
            {
                _isLanSessionActive = true;
                PatchHelper.Log("LAN host button pressed - pushing host submenu");
                PushSubmenu(__instance, typeof(NMultiplayerHostSubmenu));
            }));
            lanJoinBtn.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
            {
                _isLanSessionActive = true;
                PatchHelper.Log("LAN join button pressed - pushing join screen");
                PushSubmenu(__instance, typeof(NJoinFriendScreen));
            }));

            parent.AddChild(lanHostBtn);
            parent.AddChild(lanJoinBtn);

            // Use different hues to visually distinguish Host vs Join tiles
            const float lanHostHue = 0.55f;  // blue-green
            const float lanJoinHue = 0.12f;  // warm orange -- distinct from host
            StyleLanTile(lanHostBtn, hostBtn, Strings.T("创建局域网", "Host LAN"),
                Strings.T("通过局域网创建房间", "Host a game on your local network"),
                "res://images/ui/main_menu/submenu_host.png", lanHostHue);
            StyleLanTile(lanJoinBtn, hostBtn, Strings.T("加入局域网", "Join LAN"),
                Strings.T("通过 IP 加入局域网房间", "Join a game by IP on your local network"),
                "res://images/ui/main_menu/submenu_join.png", lanJoinHue);

            PatchHelper.Log($"LAN: native-style tiles added to '{parent.Name}' ({parent.GetType().Name}).");
        }
        catch (Exception ex)
        {
            ReportError("LAN menu buttons", ex);
        }
    }

    /// <summary>
    /// Instantiates a fresh copy of the native submenu-button scene (the same .tscn
    /// used for Host/Join). Unlike NSubmenuButton.Duplicate(), Instantiate() produces
    /// an independent node tree with its own local-to-scene ShaderMaterial, so hover/
    /// click state is NOT shared with the native tiles (dev rule #4).
    /// </summary>
    private static NSubmenuButton? CreateLanSubmenuTile(NSubmenuButton template, string nodeName)
    {
        try
        {
            var scenePath = !string.IsNullOrEmpty(template.SceneFilePath)
                ? template.SceneFilePath
                : "res://scenes/ui/submenu_button.tscn";
            var packed = ResourceLoader.Load<PackedScene>(scenePath);
            if (packed == null)
            {
                PatchHelper.Log($"LAN: could not load submenu-button scene '{scenePath}'.");
                return null;
            }
            var tile = packed.Instantiate<NSubmenuButton>();
            tile.Name = nodeName;
            tile.SizeFlagsHorizontal = template.SizeFlagsHorizontal;
            tile.SizeFlagsVertical = template.SizeFlagsVertical;
            tile.CustomMinimumSize = template.CustomMinimumSize;
            return tile;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"LAN: CreateLanSubmenuTile failed: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Applies LAN-specific styling to an instantiated submenu tile: custom title +
    /// description, a contextual icon, and a hue-shifted background. The tile's
    /// ShaderMaterial is DUPLICATED before the hue change so the shared native material
    /// is never mutated.
    /// </summary>
    private static void StyleLanTile(NSubmenuButton tile, NSubmenuButton template,
        string title, string description, string iconResPath, float hue)
    {
        try
        {
            var titleLabel = tile.GetNodeOrNull<MegaLabel>("%Title");
            if (titleLabel != null) titleLabel.SetTextAutoSize(title);

            var descLabel = tile.GetNodeOrNull<MegaRichTextLabel>("%Description");
            if (descLabel != null) descLabel.Text = description;

            var iconRect = tile.GetNodeOrNull<TextureRect>("Icon");
            if (iconRect != null)
            {
                Texture2D? tex = null;
                if (!string.IsNullOrEmpty(iconResPath))
                    tex = ResourceLoader.Load<Texture2D>(iconResPath);
                if (tex == null)
                    tex = template.GetNodeOrNull<TextureRect>("Icon")?.Texture;
                if (tex != null) iconRect.Texture = tex;
            }

            var bg = tile.GetNodeOrNull<Control>("BgPanel");
            if (bg != null && bg.Material is ShaderMaterial sharedMat)
            {
                var ownMat = (ShaderMaterial)sharedMat.Duplicate(true);
                ownMat.SetShaderParameter("h", hue);
                bg.Material = ownMat;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"LAN: StyleLanTile failed: {ex}");
        }
    }

    private static void PushSubmenu(NSubmenu submenu, Type submenuType)
    {
        try
        {
            var stackField = typeof(NSubmenu).GetField(GameInternals.Submenu_Stack,
                BindingFlags.NonPublic | BindingFlags.Instance);
            var stack = stackField?.GetValue(submenu) as NSubmenuStack;
            if (stack == null) { PatchHelper.Log("LAN: stack not found"); return; }

            var pushMethod = typeof(NSubmenuStack).GetMethod(GameInternals.SubmenuStack_PushSubmenuType, new[] { typeof(Type) });
            if (pushMethod != null)
            {
                pushMethod.Invoke(stack, new object[] { submenuType });
            }
            else
            {
                var genericMethod = typeof(NSubmenuStack).GetMethods()
                    .FirstOrDefault(m => m.Name == GameInternals.SubmenuStack_PushSubmenuType && m.IsGenericMethod);
                genericMethod?.MakeGenericMethod(submenuType)?.Invoke(stack, null);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"LAN: failed to push submenu {submenuType.Name}: {ex}");
        }
    }

}