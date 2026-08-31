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
        this.entries = this.scanner.Scan();

        this.Flags = ImGuiWindowFlags.NoTitleBar
                     | ImGuiWindowFlags.NoScrollbar
                     | ImGuiWindowFlags.NoScrollWithMouse
                     | ImGuiWindowFlags.AlwaysAutoResize
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
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

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

        if (this.entries.Count == 0)
        {
            // Keep a tiny footprint so the window still exists and can be found again.
            ImGui.Dummy(new Vector2(1, 1));
            return;
        }

        var scale = Math.Clamp(this.config.Scale, 0.25f, 4f);
        ImGui.SetWindowFontScale(scale);

        // Height is derived from the font rather than fixed, so the HP bar is always
        // tall enough to hold its own label. With the old fixed 34px box the bar came
        // out around 10px tall and the label was silently dropped, which made the
        // "show HP numbers" setting look like it did nothing.
        var boxSize = new Vector2(190f * scale, (3f * scale * 2f) + (ImGui.GetTextLineHeight() * 2f) + (2f * scale));
        var line = Math.Max(1, this.config.LineLength);
        var count = this.entries.Count;

        int cols, rows;
        if (this.config.Orientation == ListOrientation.Vertical)
        {
            cols = (count + line - 1) / line;
            rows = Math.Min(count, line);
        }
        else
        {
            rows = (count + line - 1) / line;
            cols = Math.Min(count, line);
        }

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









