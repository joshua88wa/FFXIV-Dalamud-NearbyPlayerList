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

    // Screen point the list is pinned to. ImGui positions windows by their top left,
    // so holding any other corner means repositioning every frame.
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

    // Set by the plugin so the settings button can open the config window.
    public Action? OpenConfig { get; set; }

    public void RequestCenter() => this.centerRequested = true;

    public override bool DrawConditions()
    {
        var local = Service.Objects.LocalPlayer;
        if (local == null)
            return false;

        if (!VisibilityResolver.ShouldShow(this.config, this.scanner.RaiseActions))
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
            // Skipped while shift is held; re-pinning every frame would cancel the drag.
            ImGui.SetNextWindowPos(this.anchor.Value, ImGuiCond.Always, this.Pivot);
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 2));

        // ImGui clamps every window to style.WindowMinSize, 32x32 by default, which is
        // larger than the collapsed strip. Auto-resizing windows skip the clamp,
        // explicitly sized ones do not.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1, 1));

        // Sized explicitly rather than with AlwaysAutoResize, which fits the window to the
        // previous frame's content. That lag offsets any pinned corner except the top left.
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

        var stripHeight = this.StripHeight(scale);
        var stripWidth = this.StripWidth(scale);
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

    // ImGui hit testing is rectangular, so click-through is done by dropping input for
    // the whole window on frames where the cursor is not over a player box.
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
        // Re-derive the pinned corner from where the window actually ended up.
        this.anchor = ImGui.GetWindowPos() + (ImGui.GetWindowSize() * this.Pivot);

        this.hitRects.Clear();

        // Shift makes the boxes non-interactive for this frame. Click-through leaves only a
        // couple of pixels of grabbable space, so shift makes the whole window draggable.
        this.interactive = !ImGui.GetIO().KeyShift;

        var scale = Math.Clamp(this.config.Scale, 0.25f, 4f);
        ImGui.SetWindowFontScale(scale);

        // Collapsed, or nobody nearby: the strip stands in for the list.
        if (!this.config.ShowWindow || this.entries.Count == 0)
        {
            if (this.config.ShowWindowButtons)
                this.DrawButtonRow(0f, scale);
            else
                ImGui.Dummy(new Vector2(1, 1));

            ImGui.SetWindowFontScale(1f);
            return;
        }

        // Box height derives from the font so the HP bar can always fit its own label.
        // Same helpers as CalcWindowSize, so layout and size cannot drift apart.
        var boxSize = CalcBoxSize(scale, ImGui.GetTextLineHeight());
        var line = Math.Max(1, this.config.LineLength);
        var count = this.entries.Count;
        var (cols, rows) = this.CalcGrid(count);

        // Entries go into a grid so the growth direction can flip which end fills first.
        // The first player sits in the pinned corner and later ones fill away from it.
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

        // Soft target outranks hard target: it is what an action lands on when both are set.
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

        // A box can be both soft and hard target; show the second as an inner ring.
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

        // Shadowed so the label stays readable over both the filled and empty bar.
        draw.AddText(pos + new Vector2(1f, 1f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.8f)), label);
        draw.AddText(pos, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)), label);
    }

    // Icons are drawn untinted. The 62100 set carries its own colour, and multiplying a
    // role colour over it muddies the art; role is conveyed by the HP bar instead.
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

    private enum Glyph { Close, Show, Settings, Info, FilterAll, FilterHurt, FilterDead }

    private bool ButtonsActive() => this.config.ButtonsRequire switch
    {
        ButtonModifier.Ctrl => ImGui.GetIO().KeyCtrl,
        ButtonModifier.Alt => ImGui.GetIO().KeyAlt,
        _ => true,
    };

    private float StripHeight(float scale) => 16f * scale;

    private float StripWidth(float scale)
    {
        var size = this.StripHeight(scale);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = (size * 3f) + (spacing * 2f);

        if (this.config.ShowFilterButtons && this.config.ShowWindow)
            width += (size * 0.75f) + (size * 3f) + (spacing * 2f);

        return width;
    }

    // Window buttons sit against the pinned corner and filter buttons hang off the
    // inside edge, so the hide button does not move when the filter group appears.
    private void DrawButtonRow(float contentWidth, float scale)
    {
        var size = this.StripHeight(scale);
        var rowWidth = this.StripWidth(scale);
        var gap = size * 0.75f;

        var pinnedLeft = this.config.GrowHorizontally == HorizontalGrowth.Right;
        if (!pinnedLeft)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, contentWidth - rowWidth));

        var showFilters = this.config.ShowFilterButtons && this.config.ShowWindow;

        if (pinnedLeft)
        {
            this.DrawWindowButtons(size);
            if (showFilters)
            {
                ImGui.SameLine(0f, gap);
                this.DrawFilterButtons(size);
            }
        }
        else
        {
            if (showFilters)
            {
                this.DrawFilterButtons(size);
                ImGui.SameLine(0f, gap);
            }

            this.DrawWindowButtons(size);
        }
    }

    private string ModifierHint()
    {
        var modifier = this.config.ButtonsRequire switch
        {
            ButtonModifier.Ctrl => "Ctrl",
            ButtonModifier.Alt => "Alt",
            _ => null,
        };

        return modifier == null ? string.Empty : $" Hold {modifier} to use these buttons.";
    }

    private void DrawWindowButtons(float size)
    {
        var active = this.ButtonsActive();
        var gate = this.ModifierHint();
        var hidden = !this.config.ShowWindow;

        if (this.DrawIconButton(
                "npl_close",
                size,
                active,
                hidden ? Glyph.Show : Glyph.Close,
                hidden ? "Show the list" : "Hide the list",
                hidden
                    ? "Brings the player list back." + gate
                    : (this.config.KeepButtonsWhenHidden
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

    private void DrawFilterButtons(float size)
    {
        var active = this.ButtonsActive();
        var gate = this.ModifierHint();
        var threshold = (int)Math.Round(this.config.HealthThreshold * 100f);

        // Highlights the mode actually in force, which a zone rule may be overriding.
        var current = VisibilityResolver.EffectiveFilter(this.config);

        if (this.DrawIconButton("npl_f_all", size, active, Glyph.FilterAll, "Show all players",
                "No filtering." + gate, current == FilterMode.All))
            this.SetFilter(FilterMode.All);

        ImGui.SameLine();
        if (this.DrawIconButton("npl_f_hurt", size, active, Glyph.FilterHurt, "Show the hurt",
                $"Only players at or below {threshold}% health. Dead players are below any threshold, so they show here too." + gate,
                current == FilterMode.BelowHealthThreshold))
            this.SetFilter(FilterMode.BelowHealthThreshold);

        ImGui.SameLine();
        if (this.DrawIconButton("npl_f_dead", size, active, Glyph.FilterDead, "Show only the dead",
                "The raising mode." + gate, current == FilterMode.DeadOnly))
            this.SetFilter(FilterMode.DeadOnly);
    }

    private void SetFilter(FilterMode mode)
    {
        this.config.Filter = mode;
        this.config.Save();
    }
    private bool DrawIconButton(string id, float size, bool active, Glyph glyph, string title, string body, bool selected = false)
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

        if (selected)
            draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.30f, 0.55f, 0.90f, 0.75f)), 3f);
        else if (hovered)
            draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, active ? 0.22f : 0.10f)), 3f);

        var alpha = selected || active ? 0.95f : hovered ? 0.70f : 0.38f;
        DrawGlyph(draw, glyph, min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)), size);

        if (hovered)
        {
            using var tooltip = ImRaiiTooltip();
            // ImGui.TextDisabled does not wrap.
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 18f);
            ImGui.TextUnformatted(title);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(body);
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
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

            case Glyph.FilterAll:
                DrawGlyphText(draw, "A", center, color, size);
                break;

            case Glyph.FilterHurt:
                DrawGlyphText(draw, "!", center, color, size);
                break;

            case Glyph.FilterDead:
                // Drawn rather than typed: the default font has no dependable skull glyph.
                var socket = ImGui.GetColorU32(new Vector4(0.05f, 0.05f, 0.07f, 1f));
                draw.AddCircleFilled(center - new Vector2(0f, size * 0.06f), size * 0.25f, color);
                draw.AddRectFilled(
                    center + new Vector2(-size * 0.13f, size * 0.10f),
                    center + new Vector2(size * 0.13f, size * 0.26f),
                    color,
                    size * 0.06f);
                draw.AddCircleFilled(center + new Vector2(-size * 0.10f, -size * 0.08f), size * 0.07f, socket);
                draw.AddCircleFilled(center + new Vector2(size * 0.10f, -size * 0.08f), size * 0.07f, socket);
                break;
        }
    }

    // Explicit font size so a letter always fits its button box at any list scale.
    private static void DrawGlyphText(ImDrawListPtr draw, string label, Vector2 center, uint color, float size)
    {
        var font = ImGui.GetFont();
        var fontSize = size * 0.82f;
        var measured = ImGui.CalcTextSize(label) * (fontSize / ImGui.GetFontSize());
        draw.AddText(font, fontSize, center - (measured * 0.5f), color, label);
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



























