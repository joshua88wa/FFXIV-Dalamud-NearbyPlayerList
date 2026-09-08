using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace NearbyPlayerList.Windows;

public sealed class ListWindow : Window
{
    private readonly Configuration config;
    private readonly PlayerScanner scanner;

    private readonly List<(Vector2 Min, Vector2 Max)> hitRects = new();
    private List<PlayerEntry> entries = new();

    private bool centerRequested;
    private bool dragLatch;
    private bool interactive = true;

    /// <summary>
    /// The screen point the list is pinned to. ImGui anchors auto-resizing windows by
    /// their top left corner, so growth is down and to the right unless we pin a
    /// different corner and let ImGui place the window relative to it.
    /// </summary>
    private Vector2? anchor;

    private Vector2 Pivot => new(
        this.config.GrowHorizontally == HorizontalGrowth.Left ? 1f : 0f,
        this.config.GrowVertically == VerticalGrowth.Up ? 1f : 0f);

    public ListWindow(Configuration config, PlayerScanner scanner)
        : base("Nearby Player List##NearbyPlayerListMain")
    {
        this.config = config;
        this.scanner = scanner;
        this.RespectCloseHotkey = false;
        this.DisableWindowSounds = true;
    }

    /// <summary>Set by the plugin so the settings button can open the config window.</summary>
    public Action? OpenConfig { get; set; }

    public void RequestCenter() => this.centerRequested = true;

    public override bool DrawConditions()
    {
        var local = Service.Objects.LocalPlayer;
        if (local == null)
            return false;

        var inCombat = Service.Condition[ConditionFlag.InCombat];
        var weaponOut = local.StatusFlags.HasFlag(StatusFlags.WeaponOut);

        return this.config.HideWhen switch
        {
            HideCondition.InCombat => !inCombat,
            HideCondition.OutOfCombat => inCombat,
            HideCondition.WeaponDrawn => !weaponOut,
            HideCondition.WeaponSheathed => weaponOut,
            _ => true,
        };
    }

    public override void PreDraw()
    {
        // No point scanning the object table while the list is collapsed.
        this.entries = this.config.ShowWindow ? this.scanner.Scan() : new List<PlayerEntry>();

        this.Flags = ImGuiWindowFlags.NoTitleBar
                     | ImGuiWindowFlags.NoScrollbar
                     | ImGuiWindowFlags.NoScrollWithMouse
                     | ImGuiWindowFlags.NoResize
                     | ImGuiWindowFlags.NoFocusOnAppearing
                     | ImGuiWindowFlags.NoNav
                     | ImGuiWindowFlags.NoBackground
                     | ImGuiWindowFlags.NoCollapse
                     | ImGuiWindowFlags.NoDocking;

        if (this.config.LockPosition)
            this.Flags |= ImGuiWindowFlags.NoMove;

        if (!this.ShouldAcceptInput())
            this.Flags |= ImGuiWindowFlags.NoInputs;

        if (this.centerRequested)
        {
            var viewport = ImGui.GetMainViewport();
            var center = viewport.Pos + (viewport.Size * 0.5f);
            ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
            this.anchor = null;
            this.centerRequested = false;
        }
        else if (this.anchor.HasValue && !ImGui.GetIO().KeyShift)
        {
            // Not applied while shift is held, otherwise re-pinning the window every
            // frame would cancel out the drag before it could take effect.
            ImGui.SetNextWindowPos(this.anchor.Value, ImGuiCond.Always, this.Pivot);
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 2));

        // ImGui clamps windows to style.WindowMinSize, 32x32 by default. The collapsed
        // strip is shorter than that, so ImGui was quietly enlarging the window while
        // we positioned it using the size we asked for, putting the pinned edge about
        // twelve pixels out. Auto-resizing windows skip this clamp, which is why the
        // problem only appeared once we started sizing the window ourselves.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1, 1));

        // Sized explicitly rather than with AlwaysAutoResize. Auto-resize fits the
        // window to the PREVIOUS frame's content, which is invisible when the top left
        // is pinned but makes the pinned corner jump by the height difference every
        // time the player count changes when the bottom or right edge is pinned.
        ImGui.SetNextWindowSize(this.CalcWindowSize(), ImGuiCond.Always);
    }

    private static Vector2 CalcBoxSize(float scale, float lineHeight)
        => new(190f * scale, (3f * scale * 2f) + (lineHeight * 2f) + (2f * scale));

    private (int Cols, int Rows) CalcGrid(int count)
    {
        var line = Math.Max(1, this.config.LineLength);

        return this.config.Orientation == ListOrientation.Vertical
            ? ((count + line - 1) / line, Math.Min(count, line))
            : (Math.Min(count, line), (count + line - 1) / line);
    }

    private Vector2 CalcWindowSize()
    {
        var scale = Math.Clamp(this.config.Scale, 0.25f, 4f);

        // PreDraw runs outside the window, so the window font scale is not applied yet.
        // Multiplying by it here gives the same line height Draw will see.
        var lineHeight = ImGui.GetTextLineHeight() * scale;

        var pad = ImGui.GetStyle().WindowPadding;
        var spacing = ImGui.GetStyle().ItemSpacing;
        var chrome = pad * 2f;

        var stripHeight = 16f * scale;
        var stripWidth = (16f * scale * 3f) + (spacing.X * 2f);
        var showStrip = this.config.ShowWindowButtons;

        if (!this.config.ShowWindow || this.entries.Count == 0)
        {
            return showStrip
                ? new Vector2(stripWidth, stripHeight) + chrome
                : new Vector2(1f, 1f) + chrome;
        }

        var box = CalcBoxSize(scale, lineHeight);
        var (cols, rows) = this.CalcGrid(this.entries.Count);

        var width = (cols * box.X) + ((cols - 1) * spacing.X);
        var height = (rows * box.Y) + ((rows - 1) * spacing.Y);

        if (showStrip)
        {
            width = Math.Max(width, stripWidth);
            height += stripHeight + spacing.Y;
        }

        return new Vector2(width, height) + chrome;
    }

    public override void PostDraw() => ImGui.PopStyleVar(3);

    /// <summary>
    /// ImGui hit testing is rectangular, so "click through the gaps" is done by
    /// dropping input for the whole window on frames where the cursor is not over
    /// one of last frame's player boxes. Holding shift re-enables input so the
    /// window can still be dragged by its empty space.
    /// </summary>
    private bool ShouldAcceptInput()
    {
        var io = ImGui.GetIO();

        if (this.dragLatch)
        {
            if (!io.MouseDown[0])
                this.dragLatch = false;
            return true;
        }

        if (io.KeyShift)
        {
            if (io.MouseDown[0])
                this.dragLatch = true;
            return true;
        }

        var mouse = io.MousePos;
        foreach (var rect in this.hitRects)
        {
            if (mouse.X >= rect.Min.X && mouse.X <= rect.Max.X &&
                mouse.Y >= rect.Min.Y && mouse.Y <= rect.Max.Y)
                return true;
        }

        return false;
    }

    public override void Draw()
    {
        // Re-derive the pinned corner from wherever the window actually ended up, so a
        // drag, a centre, or a change of growth direction all just work.
        this.anchor = ImGui.GetWindowPos() + (ImGui.GetWindowSize() * this.Pivot);

        this.hitRects.Clear();

        // Holding shift turns the boxes into non-interactive items for this frame.
        // With click-through on, the real empty space is only the couple of pixels
        // between boxes, which is not something anyone can reliably grab, so shift
        // makes the entire window draggable instead.
        this.interactive = !ImGui.GetIO().KeyShift;

        var scale = Math.Clamp(this.config.Scale, 0.25f, 4f);
        ImGui.SetWindowFontScale(scale);

        // Collapsed: the strip alone stands in for the list, and its first button
        // restores it. Also covers the case where the list is on but nobody is nearby.
        if (!this.config.ShowWindow || this.entries.Count == 0)
        {
            if (this.config.ShowWindowButtons)
                this.DrawButtonRow(0f, scale);
            else
                ImGui.Dummy(new Vector2(1, 1));

            ImGui.SetWindowFontScale(1f);
            return;
        }

        // Height is derived from the font rather than fixed, so the HP bar is always
        // tall enough to hold its own label. With the old fixed 34px box the bar came
        // out around 10px tall and the label was silently dropped, which made the
        // "show HP numbers" setting look like it did nothing.
        // Same helpers the window size calculation uses, so layout and size cannot
        // drift apart and clip the last row.
        var boxSize = CalcBoxSize(scale, ImGui.GetTextLineHeight());
        var line = Math.Max(1, this.config.LineLength);
        var count = this.entries.Count;
        var (cols, rows) = this.CalcGrid(count);

        // Entries are placed into a grid rather than drawn straight down the list, so
        // that the growth direction can flip which end of the grid gets filled first.
        // The first player always sits in the pinned corner and later ones fill away
        // from it, which is the whole point of choosing a direction: the boxes you have
        // already learned the position of do not move when someone new shows up.
        var grid = new PlayerEntry?[rows, cols];
        for (var i = 0; i < count; i++)
        {
            int col, row;
            if (this.config.Orientation == ListOrientation.Vertical)
            {
                col = i / line;
                row = i % line;
            }
            else
            {
                row = i / line;
                col = i % line;
            }

            if (this.config.GrowHorizontally == HorizontalGrowth.Left)
                col = cols - 1 - col;
            if (this.config.GrowVertically == VerticalGrowth.Up)
                row = rows - 1 - row;

            grid[row, col] = this.entries[i];
        }

        var contentWidth = (cols * boxSize.X) + ((cols - 1) * ImGui.GetStyle().ItemSpacing.X);
        var buttonsFirst = this.config.GrowVertically == VerticalGrowth.Down;

        if (this.config.ShowWindowButtons && buttonsFirst)
            this.DrawButtonRow(contentWidth, scale);

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                if (c > 0)
                    ImGui.SameLine();

                var entry = grid[r, c];
                if (entry == null)
                    ImGui.Dummy(boxSize);   // keeps a partial column or row aligned
                else
                    this.DrawEntry(entry, boxSize, scale);
            }
        }

        if (this.config.ShowWindowButtons && !buttonsFirst)
            this.DrawButtonRow(contentWidth, scale);

        ImGui.SetWindowFontScale(1f);
    }

    private void DrawEntry(PlayerEntry entry, Vector2 boxSize, float scale)
    {
        if (this.interactive)
            ImGui.InvisibleButton($"##npl_{entry.EntityId}", boxSize);
        else
            ImGui.Dummy(boxSize);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        this.hitRects.Add((min, max));

        var hovered = this.interactive && ImGui.IsItemHovered();

        if (this.interactive)
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                Act(this.config.LeftClick, entry);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                Act(this.config.RightClick, entry);
        }

        var draw = ImGui.GetWindowDrawList();
        var rounding = 4f * scale;

        // Selection state first, since that is the thing you most need to spot at a
        // glance. Soft target outranks the hard target here because it is what an
        // action will actually land on when both are set.
        Vector4? highlight = null;
        if (this.config.HighlightSoftTarget && entry.IsSoftTarget)
            highlight = this.config.SoftTargetColor;
        else if (this.config.HighlightTarget && entry.IsTarget)
            highlight = this.config.TargetColor;
        else if (this.config.HighlightFocusTarget && entry.IsFocusTarget)
            highlight = this.config.FocusTargetColor;

        var background = entry.IsDead
            ? new Vector4(0.30f, 0.30f, 0.30f, 0.75f)
            : new Vector4(0.06f, 0.06f, 0.08f, 0.75f);
        if (hovered)
            background.W = 0.92f;

        if (highlight.HasValue && this.config.HighlightTintBackground)
        {
            var h = highlight.Value;
            background = new Vector4(
                (background.X * 0.60f) + (h.X * 0.40f),
                (background.Y * 0.60f) + (h.Y * 0.40f),
                (background.Z * 0.60f) + (h.Z * 0.40f),
                Math.Min(1f, background.W + 0.10f));
        }

        draw.AddRectFilled(min, max, ImGui.GetColorU32(background), rounding);

        // Borders are stacked inward so overlapping states stay readable, for example
        // a party member who is also your current target.
        var rings = new List<(Vector4 Color, float Thickness)>();

        if (highlight.HasValue)
            rings.Add((highlight.Value, 2.5f));

        // A box can be both the soft target and the hard target. Show the second one
        // as an inner ring rather than dropping it.
        if (this.config.HighlightTarget && entry.IsTarget && highlight.HasValue && highlight.Value != this.config.TargetColor)
            rings.Add((this.config.TargetColor, 2f));

        if (entry.IsPartyMember && this.config.HighlightParty)
            rings.Add((this.config.PartyColor, 2f));

        if (rings.Count == 0 && hovered)
            rings.Add((new Vector4(1f, 1f, 1f, 0.6f), 1f));

        var inset = 0f;
        foreach (var ring in rings)
        {
            var t = ring.Thickness * scale;
            var offset = new Vector2(inset, inset);
            draw.AddRect(min + offset, max - offset, ImGui.GetColorU32(ring.Color), Math.Max(0f, rounding - inset), ImDrawFlags.None, t);
            inset += t;
        }

        var roleColor = RoleColor(entry.Role);
        if (entry.IsDead)
            roleColor = new Vector4(0.55f, 0.55f, 0.55f, 1f);

        var pad = 3f * scale;
        var iconSide = boxSize.Y - (pad * 2);
        var iconMin = new Vector2(min.X + pad, min.Y + pad);
        var iconMax = iconMin + new Vector2(iconSide, iconSide);

        DrawJobIcon(draw, entry, iconMin, iconMax, scale);

        draw.PushClipRect(min, max, true);

        var textLeft = iconMax.X + (4f * scale);
        var lineHeight = ImGui.GetTextLineHeight();

        var nameColor = entry.IsDead
            ? new Vector4(0.75f, 0.75f, 0.75f, 1f)
            : new Vector4(1f, 1f, 1f, 1f);
        draw.AddText(new Vector2(textLeft, min.Y + pad), ImGui.GetColorU32(nameColor), entry.Name);

        // HP bar
        var barMin = new Vector2(textLeft, min.Y + pad + lineHeight + (1f * scale));
        var barMax = new Vector2(max.X - pad, max.Y - pad);
        if (barMax.Y > barMin.Y && barMax.X > barMin.X)
            this.DrawHpBar(draw, entry, barMin, barMax, roleColor, scale);

        draw.PopClipRect();

        if (hovered)
        {
            using var tooltip = ImRaiiTooltip();
            ImGui.Text($"{entry.Name}");
            ImGui.Text($"{entry.CurrentHp:N0} / {entry.MaxHp:N0} HP");
            ImGui.Text($"{entry.Distance:F1} yalms away");
        }
    }

    private static IDisposable ImRaiiTooltip()
    {
        ImGui.BeginTooltip();
        return new TooltipScope();
    }

    private sealed class TooltipScope : IDisposable
    {
        public void Dispose() => ImGui.EndTooltip();
    }

    private void DrawHpBar(ImDrawListPtr draw, PlayerEntry entry, Vector2 barMin, Vector2 barMax, Vector4 roleColor, float scale)
    {
        var rounding = 2f * scale;
        draw.AddRectFilled(barMin, barMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), rounding);

        var fraction = Math.Clamp(entry.HpFraction, 0f, 1f);
        if (fraction > 0f)
        {
            var fillMax = new Vector2(barMin.X + ((barMax.X - barMin.X) * fraction), barMax.Y);
            draw.AddRectFilled(barMin, fillMax, ImGui.GetColorU32(roleColor), rounding);
        }

        string label;
        if (entry.IsDead && this.config.ShowRaiseInProgress && entry.RaisedBy != null)
            label = $"Being raised by {entry.RaisedBy}";
        else if (entry.IsDead && entry.AlreadyRaised)
            label = "Raised";
        else if (entry.IsDead)
            label = "Dead";
        else if (this.config.ShowHpNumbers)
            label = $"{entry.CurrentHp:N0} / {entry.MaxHp:N0}";
        else
            label = $"{fraction * 100f:F0}%";

        var textSize = ImGui.CalcTextSize(label);
        var barHeight = barMax.Y - barMin.Y;
        var pos = new Vector2(barMin.X + (3f * scale), barMin.Y + ((barHeight - textSize.Y) * 0.5f));

        // Drawn with a shadow rather than gated on fitting, so it stays readable over
        // both the filled and unfilled parts of the bar.
        draw.AddText(pos + new Vector2(1f, 1f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.8f)), label);
        draw.AddText(pos, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)), label);
    }

    /// <summary>
    /// Icons are drawn as the game authored them. Role colour is carried by the HP bar
    /// instead: the 62100 icon set has plenty of colour of its own, and multiplying a
    /// role colour over it only muddies the artwork without adding information.
    /// </summary>
    private static void DrawJobIcon(ImDrawListPtr draw, PlayerEntry entry, Vector2 min, Vector2 max, float scale)
    {
        var tint = entry.IsDead
            ? new Vector4(0.55f, 0.55f, 0.55f, 1f)
            : Vector4.One;

        if (entry.JobId == 0)
        {
            draw.AddRectFilled(min, max, ImGui.GetColorU32(tint), 3f * scale);
            return;
        }

        try
        {
            var texture = Service.Textures.GetFromGameIcon(new GameIconLookup(62100 + entry.JobId)).GetWrapOrEmpty();
            draw.AddImage(texture.Handle, min, max, Vector2.Zero, Vector2.One, ImGui.GetColorU32(tint));
        }
        catch
        {
            draw.AddRectFilled(min, max, ImGui.GetColorU32(tint), 3f * scale);
        }
    }

    private enum Glyph { Close, Show, Settings, Info }

    private bool ButtonsActive() => this.config.ButtonsRequire switch
    {
        ButtonModifier.Ctrl => ImGui.GetIO().KeyCtrl,
        ButtonModifier.Alt => ImGui.GetIO().KeyAlt,
        _ => true,
    };

    /// <summary>
    /// A small control strip pinned to the same corner the list grows away from, so it
    /// stays put as players come and go. Hovering always works, so the reminder is
    /// discoverable, but clicking needs a modifier: these sit next to boxes you click
    /// constantly, and hiding the list by accident mid-fight would be miserable.
    /// </summary>
    private void DrawButtonRow(float contentWidth, float scale)
    {
        var size = 16f * scale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rowWidth = (size * 3f) + (spacing * 2f);

        if (this.config.GrowHorizontally == HorizontalGrowth.Left)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, contentWidth - rowWidth));

        var active = this.ButtonsActive();
        var modifier = this.config.ButtonsRequire switch
        {
            ButtonModifier.Ctrl => "Ctrl",
            ButtonModifier.Alt => "Alt",
            _ => null,
        };

        var gate = modifier == null ? string.Empty : $" Hold {modifier} to use these buttons.";

        var hidden = !this.config.ShowWindow;
        var restorable = this.config.KeepButtonsWhenHidden;

        if (this.DrawIconButton(
                "npl_close",
                size,
                active,
                hidden ? Glyph.Show : Glyph.Close,
                hidden ? "Show the list" : "Hide the list",
                hidden
                    ? "Brings the player list back." + gate
                    : (restorable
                        ? "Leaves these buttons behind so you can bring it back." + gate
                        : "Type /npl to bring it back." + gate)))
        {
            this.config.ShowWindow = !this.config.ShowWindow;
            this.config.Save();
        }

        ImGui.SameLine();
        if (this.DrawIconButton("npl_config", size, active, Glyph.Settings, "Settings", "Same as typing /npl config." + gate))
            this.OpenConfig?.Invoke();

        ImGui.SameLine();
        this.DrawIconButton("npl_info", size, active, Glyph.Info, "Moving the list",
            "Hold Shift and drag from anywhere on the list, including from on top of a player box. /npl center brings it back if it ends up off screen.");
    }

    private bool DrawIconButton(string id, float size, bool active, Glyph glyph, string title, string body)
    {
        if (this.interactive)
            ImGui.InvisibleButton($"##{id}", new Vector2(size, size));
        else
            ImGui.Dummy(new Vector2(size, size));

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        this.hitRects.Add((min, max));

        var hovered = this.interactive && ImGui.IsItemHovered();
        var clicked = active && hovered && ImGui.IsItemClicked(ImGuiMouseButton.Left);

        var draw = ImGui.GetWindowDrawList();

        if (hovered)
            draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, active ? 0.22f : 0.10f)), 3f);

        var alpha = active ? 0.95f : hovered ? 0.70f : 0.38f;
        DrawGlyph(draw, glyph, min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)), size);

        if (hovered)
        {
            using var tooltip = ImRaiiTooltip();
            ImGui.Text(title);
            ImGui.TextDisabled(body);
        }

        return clicked;
    }

    private static void DrawGlyph(ImDrawListPtr draw, Glyph glyph, Vector2 min, Vector2 max, uint color, float size)
    {
        var pad = size * 0.28f;
        var a = min + new Vector2(pad, pad);
        var b = max - new Vector2(pad, pad);
        var thickness = Math.Max(1f, size * 0.10f);
        var center = (min + max) * 0.5f;

        switch (glyph)
        {
            case Glyph.Close:
                draw.AddLine(a, b, color, thickness);
                draw.AddLine(new Vector2(b.X, a.Y), new Vector2(a.X, b.Y), color, thickness);
                break;

            case Glyph.Show:
                draw.AddLine(new Vector2(a.X, center.Y), new Vector2(b.X, center.Y), color, thickness);
                draw.AddLine(new Vector2(center.X, a.Y), new Vector2(center.X, b.Y), color, thickness);
                break;

            case Glyph.Settings:
                for (var i = 0; i < 3; i++)
                {
                    var y = a.Y + ((b.Y - a.Y) * i * 0.5f);
                    draw.AddLine(new Vector2(a.X, y), new Vector2(b.X, y), color, thickness);
                }

                break;

            case Glyph.Info:
                draw.AddCircle(center, (size * 0.5f) - pad + (size * 0.10f), color, 0, thickness);
                draw.AddLine(new Vector2(center.X, center.Y - (size * 0.14f)), new Vector2(center.X, center.Y + (size * 0.16f)), color, thickness);
                draw.AddCircleFilled(new Vector2(center.X, center.Y - (size * 0.24f)), thickness * 0.6f, color);
                break;
        }
    }

    private static Vector4 RoleColor(byte role) => role switch
    {
        1 => new Vector4(0.20f, 0.48f, 0.85f, 1f),   // tank
        4 => new Vector4(0.20f, 0.70f, 0.35f, 1f),   // healer
        2 => new Vector4(0.78f, 0.26f, 0.26f, 1f),   // melee dps
        3 => new Vector4(0.78f, 0.26f, 0.26f, 1f),   // ranged dps
        _ => new Vector4(0.60f, 0.60f, 0.60f, 1f),
    };

    private static void Act(ClickAction action, PlayerEntry entry)
    {
        switch (action)
        {
            case ClickAction.HardTarget:
                Service.Targets.Target = entry.GameObject;
                break;
            case ClickAction.SoftTarget:
                Service.Targets.SoftTarget = entry.GameObject;
                break;
            case ClickAction.FocusTarget:
                Service.Targets.FocusTarget = entry.GameObject;
                break;
        }
    }
}

















