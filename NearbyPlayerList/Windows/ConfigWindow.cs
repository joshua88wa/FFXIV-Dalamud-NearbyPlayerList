using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NearbyPlayerList.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly ListWindow list;

    public ConfigWindow(Configuration config, ListWindow list)
        : base("Nearby Player List - Settings##NearbyPlayerListConfig")
    {
        this.config = config;
        this.list = list;
        this.Size = new Vector2(440, 560);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var dirty = false;

        if (ImGui.BeginTabBar("##npl_tabs"))
        {
            if (ImGui.BeginTabItem("Window"))
            {
                dirty |= this.DrawWindowTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Layout"))
            {
                dirty |= this.DrawLayoutTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Filtering"))
            {
                dirty |= this.DrawFilterTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Sorting"))
            {
                dirty |= this.DrawSortTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Other"))
            {
                dirty |= this.DrawOtherTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (dirty)
            this.config.Save();
    }

    private bool DrawWindowTab()
    {
        var dirty = false;

        TextHint("Moving the list: hold Shift and drag it from anywhere, including from on top of a player box. While Shift is held the boxes stop taking clicks, so you cannot target someone by accident while repositioning. Everything except the boxes is click-through, so the list never eats a click meant for the game.");
        ImGui.Separator();

        var show = this.config.ShowWindow;
        if (ImGui.Checkbox("Show the player list", ref show))
        {
            this.config.ShowWindow = show;
            dirty = true;
        }

        ImGui.SetNextItemWidth(220);
        var hideWhen = (int)this.config.HideWhen;
        if (ImGui.Combo("Hide the list", ref hideWhen, "Never\0In combat\0Out of combat\0Weapon drawn\0Weapon sheathed\0"))
        {
            this.config.HideWhen = (HideCondition)hideWhen;
            dirty = true;
        }

        ImGui.Separator();

        var locked = this.config.LockPosition;
        if (ImGui.Checkbox("Lock list position", ref locked))
        {
            this.config.LockPosition = locked;
            dirty = true;
        }

        var showButtons = this.config.ShowWindowButtons;
        if (ImGui.Checkbox("Show window buttons", ref showButtons))
        {
            this.config.ShowWindowButtons = showButtons;
            dirty = true;
        }

        if (showButtons)
        {
            ImGui.Indent();
            TextHint("A small hide, settings and help strip, pinned to the corner the list grows away from. Hovering always works; clicking needs the modifier below, so you cannot hide the list by misclicking next to a player box.");

            ImGui.SetNextItemWidth(220);
            var mod = (int)this.config.ButtonsRequire;
            if (ImGui.Combo("Buttons require", ref mod, "Ctrl\0Alt\0No modifier\0"))
            {
                this.config.ButtonsRequire = (ButtonModifier)mod;
                dirty = true;
            }

            TextHint("Shift is not offered here because it already moves the list.");

            var keep = this.config.KeepButtonsWhenHidden;
            if (ImGui.Checkbox("Keep the buttons when the list is hidden", ref keep))
            {
                this.config.KeepButtonsWhenHidden = keep;
                dirty = true;
            }

            TextHint(keep
                ? "Hiding the list leaves the buttons behind, so the first one becomes a show and hide toggle. Useful for dropping the list in a dungeon and bringing it back for a FATE without typing a command."
                : "Hiding the list hides the buttons with it, so /npl is the only way back.");

            ImGui.Unindent();
        }

        ImGui.Separator();

        // Typed value with step buttons, plus a reset. No slider, because picking an
        // exact number on a slider is fiddly.
        var scalePercent = (int)Math.Round(this.config.Scale * 100f);
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt("Scale (%)", ref scalePercent, 5, 25))
        {
            scalePercent = Math.Clamp(scalePercent, 25, 400);
            this.config.Scale = scalePercent / 100f;
            dirty = true;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Reset##scale"))
        {
            this.config.Scale = 1.0f;
            dirty = true;
        }

        var maxPlayers = this.config.MaxPlayers;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt("Max players listed", ref maxPlayers, 1, 5))
        {
            this.config.MaxPlayers = Math.Max(0, maxPlayers);
            dirty = true;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("(0 = unlimited)");

        ImGui.Separator();

        if (ImGui.Button("Center player list"))
            this.list.RequestCenter();

        TextHint("Use this if the window ends up off screen. The command /npl center does the same thing.");

        ImGui.Spacing();

        // Ctrl guard, because there is no undo for this.
        var ctrl = ImGui.GetIO().KeyCtrl;
        if (!ctrl)
            ImGui.BeginDisabled();

        if (ImGui.Button("Reset all settings to defaults"))
        {
            this.config.ResetToDefaults();
            dirty = true;
        }

        if (!ctrl)
            ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextDisabled("(hold Ctrl)");

        return dirty;
    }
    private bool DrawLayoutTab()
    {
        var dirty = false;

        ImGui.SetNextItemWidth(220);
        var orientation = (int)this.config.Orientation;
        if (ImGui.Combo("Direction", ref orientation, "Vertical\0Horizontal\0"))
        {
            this.config.Orientation = (ListOrientation)orientation;
            dirty = true;
        }

        var label = this.config.Orientation == ListOrientation.Vertical
            ? "Rows per column"
            : "Columns per row";

        var lineLength = this.config.LineLength;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt(label, ref lineLength, 1, 5))
        {
            this.config.LineLength = Math.Clamp(lineLength, 1, 40);
            dirty = true;
        }

        ImGui.Separator();

        ImGui.TextWrapped("Growth direction pins one corner of the list. The first player sits in that corner and later ones fill away from it, so boxes you have already learned the position of do not move when someone new shows up.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(220);
        var growH = (int)this.config.GrowHorizontally;
        if (ImGui.Combo("Expand horizontally", ref growH, "To the right\0To the left\0"))
        {
            this.config.GrowHorizontally = (HorizontalGrowth)growH;
            dirty = true;
        }

        ImGui.SetNextItemWidth(220);
        var growV = (int)this.config.GrowVertically;
        if (ImGui.Combo("Expand vertically", ref growV, "Downward\0Upward\0"))
        {
            this.config.GrowVertically = (VerticalGrowth)growV;
            dirty = true;
        }

        TextHint(this.config.Orientation == ListOrientation.Vertical
            ? "Vertical lists add new columns, so the horizontal setting is the one that matters most."
            : "Horizontal lists add new rows, so the vertical setting is the one that matters most.");

        ImGui.Separator();

        var showHp = this.config.ShowHpNumbers;
        if (ImGui.Checkbox("Show HP numbers instead of a percentage", ref showHp))
        {
            this.config.ShowHpNumbers = showHp;
            dirty = true;
        }

        TextHint("Changes the label written on top of each HP bar, from 75% to 12,345 / 16,000. The bar itself is unaffected, and dead players show their raise state either way.");

        return dirty;
    }

    private bool DrawFilterTab()
    {
        var dirty = false;

        var filter = (int)this.config.Filter;
        if (ImGui.RadioButton("Show all players", ref filter, (int)FilterMode.All))
            dirty = true;
        if (ImGui.RadioButton("Show only players at or below a health threshold", ref filter, (int)FilterMode.BelowHealthThreshold))
            dirty = true;

        if ((FilterMode)filter == FilterMode.BelowHealthThreshold)
        {
            ImGui.Indent();
            var threshold = (int)Math.Round(this.config.HealthThreshold * 100f);
            ImGui.SetNextItemWidth(160);
            if (ImGui.InputInt("Threshold (%)", ref threshold, 5, 10))
            {
                this.config.HealthThreshold = Math.Clamp(threshold, 1, 100) / 100f;
                dirty = true;
            }

            ImGui.Unindent();
        }

        if (ImGui.RadioButton("Show only dead players", ref filter, (int)FilterMode.DeadOnly))
            dirty = true;

        if (filter != (int)this.config.Filter)
        {
            this.config.Filter = (FilterMode)filter;
            dirty = true;
        }

        if (this.config.Filter != FilterMode.All)
        {
            ImGui.Indent();
            var ignoreRaised = this.config.FilterIgnoreAlreadyRaised;
            if (ImGui.Checkbox("Ignore players who already have a raise incoming", ref ignoreRaised))
            {
                this.config.FilterIgnoreAlreadyRaised = ignoreRaised;
                dirty = true;
            }

            ImGui.Unindent();
        }

        ImGui.Separator();

        var hideSelf = this.config.HideSelf;
        if (ImGui.Checkbox("Hide self", ref hideSelf))
        {
            this.config.HideSelf = hideSelf;
            dirty = true;
        }

        var hideParty = this.config.HideParty;
        if (ImGui.Checkbox("Hide party members", ref hideParty))
        {
            this.config.HideParty = hideParty;
            if (hideParty)
                this.config.PartyAtTop = false;
            dirty = true;
        }

        ImGui.Separator();

        var limitDistance = this.config.LimitDistance;
        if (ImGui.Checkbox("Limit by distance", ref limitDistance))
        {
            this.config.LimitDistance = limitDistance;
            dirty = true;
        }

        if (limitDistance)
        {
            ImGui.Indent();
            var distance = (int)Math.Round(this.config.MaxDistance);
            ImGui.SetNextItemWidth(160);
            if (ImGui.InputInt("Max distance (yalms)", ref distance, 1, 5))
            {
                this.config.MaxDistance = Math.Clamp(distance, 1, 200);
                dirty = true;
            }

            ImGui.Unindent();
        }

        return dirty;
    }

    private bool DrawSortTab()
    {
        var dirty = false;

        var sort = (int)this.config.Sort;
        if (ImGui.RadioButton("By role (tank, healer, dps)", ref sort, (int)SortMode.Role))
            dirty = true;
        if (ImGui.RadioButton("Alphabetically", ref sort, (int)SortMode.Alphabetical))
            dirty = true;
        if (ImGui.RadioButton("By missing HP (percentage)", ref sort, (int)SortMode.MissingHp))
            dirty = true;

        if (sort != (int)this.config.Sort)
        {
            this.config.Sort = (SortMode)sort;
            dirty = true;
        }

        ImGui.Separator();

        if (this.config.HideParty)
        {
            TextHint("Party at top is unavailable while party members are hidden.");
        }
        else
        {
            var partyTop = this.config.PartyAtTop;
            if (ImGui.Checkbox("Party members at top", ref partyTop))
            {
                this.config.PartyAtTop = partyTop;
                dirty = true;
            }

            if (partyTop)
            {
                ImGui.Indent();
                TextHint("You count as party for this, including when solo.");

                var selfFirst = this.config.SelfAboveParty;
                if (ImGui.Checkbox("Yourself above other party members", ref selfFirst))
                {
                    this.config.SelfAboveParty = selfFirst;
                    dirty = true;
                }

                ImGui.Unindent();
            }
        }

        var deadTop = this.config.DeadAtTop;
        if (ImGui.Checkbox("Dead players at top", ref deadTop))
        {
            this.config.DeadAtTop = deadTop;
            dirty = true;
        }

        if (deadTop)
        {
            ImGui.Indent();
            var ignoreRaised = this.config.SortIgnoreAlreadyRaised;
            if (ImGui.Checkbox("Do not promote players who already have a raise incoming", ref ignoreRaised))
            {
                this.config.SortIgnoreAlreadyRaised = ignoreRaised;
                dirty = true;
            }

            ImGui.Unindent();
        }

        return dirty;
    }

    private bool DrawOtherTab()
    {
        var dirty = false;

        ImGui.TextWrapped("Highlighting shows which player you already have selected. The border colour is drawn around the box, and optionally blended into its background.");
        ImGui.Spacing();

        dirty |= DrawHighlight("Highlight your target", ref this.config.HighlightTarget, ref this.config.TargetColor, "target");
        dirty |= DrawHighlight("Highlight your soft target", ref this.config.HighlightSoftTarget, ref this.config.SoftTargetColor, "soft");
        dirty |= DrawHighlight("Highlight your focus target", ref this.config.HighlightFocusTarget, ref this.config.FocusTargetColor, "focus");
        dirty |= DrawHighlight("Highlight party members", ref this.config.HighlightParty, ref this.config.PartyColor, "party");

        var tint = this.config.HighlightTintBackground;
        if (ImGui.Checkbox("Also tint the box background", ref tint))
        {
            this.config.HighlightTintBackground = tint;
            dirty = true;
        }

        TextHint("If a player is both your soft target and your hard target, the soft target colour is drawn outside and the target colour inside.");

        ImGui.Separator();

        var showRaise = this.config.ShowRaiseInProgress;
        if (ImGui.Checkbox("Show when a player is being raised by someone else", ref showRaise))
        {
            this.config.ShowRaiseInProgress = showRaise;
            dirty = true;
        }

        ImGui.Separator();

        ImGui.SetNextItemWidth(220);
        var left = (int)this.config.LeftClick;
        if (ImGui.Combo("Left click", ref left, "Do nothing\0Target\0Soft target\0Focus target\0"))
        {
            this.config.LeftClick = (ClickAction)left;
            dirty = true;
        }

        ImGui.SetNextItemWidth(220);
        var right = (int)this.config.RightClick;
        if (ImGui.Combo("Right click", ref right, "Do nothing\0Target\0Soft target\0Focus target\0"))
        {
            this.config.RightClick = (ClickAction)right;
            dirty = true;
        }

        ImGui.Separator();
        TextHint("/npl toggles the list, /npl config opens this window.");

        return dirty;
    }

    /// <summary>
    /// ImGui.TextDisabled does not wrap, so longer hints ran off the edge of the
    /// settings window and got clipped.
    /// </summary>
    private static void TextHint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private static bool DrawHighlight(string label, ref bool enabled, ref Vector4 colour, string id)
    {
        var dirty = false;

        if (ImGui.Checkbox(label, ref enabled))
            dirty = true;

        if (enabled)
        {
            ImGui.SameLine();
            if (ImGui.ColorEdit4($"##npl_colour_{id}", ref colour, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaPreviewHalf))
                dirty = true;
        }

        return dirty;
    }
}















