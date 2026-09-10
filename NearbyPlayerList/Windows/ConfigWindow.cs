using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NearbyPlayerList.Windows;

public sealed class ConfigWindow : Window
{
    private const string IssueUrl = "https://github.com/joshua88wa/FFXIV-Dalamud-NearbyPlayerList/issues";

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

    // Set by the strip so its settings button lands on the tab that explains whatever
    // it was showing, instead of dropping you on Window to hunt for it.
    private string? requestedTab;

    public void RequestTab(string tab)
    {
        this.requestedTab = tab;
        this.IsOpen = true;
    }

    private ImGuiTabItemFlags TabFlags(string tab)
    {
        if (this.requestedTab != tab)
            return ImGuiTabItemFlags.None;

        this.requestedTab = null;
        return ImGuiTabItemFlags.SetSelected;
    }

    public override void Draw()
    {
        var dirty = false;

        if (ImGui.BeginTabBar("##npl_tabs"))
        {
            if (ImGui.BeginTabItem("Window", this.TabFlags("Window")))
            {
                dirty |= this.DrawWindowTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Visibility", this.TabFlags("Visibility")))
            {
                dirty |= this.DrawVisibilityTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Layout", this.TabFlags("Layout")))
            {
                dirty |= this.DrawLayoutTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Filtering", this.TabFlags("Filtering")))
            {
                dirty |= this.DrawFilterTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Sorting", this.TabFlags("Sorting")))
            {
                dirty |= this.DrawSortTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Other", this.TabFlags("Other")))
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

        HelpMarker("A hide, settings and help strip pinned to the corner the list grows away from. Hovering always works; clicking needs the modifier below, so you cannot hide the list by misclicking next to a player box.");

        if (showButtons)
        {
            ImGui.Indent();
            ImGui.SetNextItemWidth(220);
            var mod = (int)this.config.ButtonsRequire;
            if (ImGui.Combo("Buttons require", ref mod, "Ctrl\0Alt\0No modifier\0"))
            {
                this.config.ButtonsRequire = (ButtonModifier)mod;
                dirty = true;
            }

            HelpMarker("Shift is not offered here because it already moves the list.");


            var keep = this.config.KeepButtonsWhenHidden;
            if (ImGui.Checkbox("Keep the buttons when the list is hidden", ref keep))
            {
                this.config.KeepButtonsWhenHidden = keep;
                dirty = true;
            }

            HelpMarker(keep
                ? "Hiding the list leaves the buttons behind, so the first one becomes a show and hide toggle. Useful for dropping the list in a dungeon and bringing it back for a FATE without typing a command."
                : "Hiding the list hides the buttons with it, so /npl is the only way back.");

            var filters = this.config.ShowFilterButtons;
            if (ImGui.Checkbox("Show filter mode buttons", ref filters))
            {
                this.config.ShowFilterButtons = filters;
                dirty = true;
            }

            HelpMarker("Adds A, ! and a skull to the strip for show all, show the hurt, and show only the dead. The current mode is highlighted, in its own group separated from the window buttons.");

            ImGui.Unindent();
        }

        ImGui.Separator();

        // Typed with step buttons rather than a slider: landing on an exact value with a
        // slider is fiddly.
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

        HelpMarker("Use this if the window ends up off screen. The command /npl center does the same thing.");

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

        ImGui.Separator();
        TextHint("Shift + drag moves the list, from anywhere on it including a player box");
        TextHint("While Shift is held the boxes ignore clicks, so you cannot target by accident");

        TextHint("/npl toggles the list");
        TextHint("/npl config opens this window");
        TextHint("/npl center brings the list back on screen");

        return dirty;
    }
    private bool DrawVisibilityTab()
    {
        var dirty = false;

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
        dirty |= this.DrawZoneRules();
        ImGui.Separator();
        dirty |= this.DrawJobRule();

        ImGui.Separator();
        TextHint("The list is always disabled in PvP, and that is not configurable.");

        return dirty;
    }

    private bool DrawZoneRules()
    {
        var dirty = false;

        var useZones = this.config.UseZoneRules;
        if (ImGui.Checkbox("Change visibility by zone", ref useZones))
        {
            this.config.UseZoneRules = useZones;
            dirty = true;
        }

        HelpMarker("Zones are classified from the game's own intended-use field. Anything not recognised falls into Everything else rather than being guessed at, so new content behaves predictably until the mapping is updated.");

        if (!useZones)
            return dirty;

        ImGui.Indent();
        TextHint($"Currently in: {ZoneClassifier.Label(ZoneClassifier.Current())}");
        ImGui.Spacing();

        if (ImGui.BeginTable("##npl_zones", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGui.TableSetupColumn("List", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableSetupColumn("Filter mode", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGui.TableHeadersRow();

            foreach (var category in ZoneClassifier.Configurable())
            {
                if (!this.config.ZoneRules.TryGetValue(category, out var rule))
                {
                    rule = new ZoneRule();
                    this.config.ZoneRules[category] = rule;
                }

                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(ZoneClassifier.Label(category));

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var vis = (int)rule.Visibility;
                if (ImGui.Combo($"##vis_{category}", ref vis, "Default\0Show\0Hide\0"))
                {
                    rule.Visibility = (ZoneVisibility)vis;
                    dirty = true;
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var filter = (int)rule.Filter;
                if (ImGui.Combo($"##filt_{category}", ref filter, "No change\0Show all\0Show the hurt\0Show only dead\0"))
                {
                    rule.Filter = (ZoneFilterOverride)filter;
                    dirty = true;
                }
            }

            ImGui.EndTable();
        }

        TextHint("A zone set to Show overrides the job rule below. A filter override does not change your saved filter mode, so the filter buttons go back to it when you leave.");
        ImGui.Unindent();

        return dirty;
    }

    private bool DrawJobRule()
    {
        var dirty = false;

        var useJobs = this.config.UseJobRule;
        if (ImGui.Checkbox("Change visibility by job", ref useJobs))
        {
            this.config.UseJobRule = useJobs;
            dirty = true;
        }

        HelpMarker("Hides the list entirely on jobs you have not ticked, so it does not sit on screen while you are playing something that cannot help.");

        if (!useJobs)
            return dirty;

        ImGui.Indent();

        var anyRaise = this.config.JobRuleAnyRaise;
        if (ImGui.Checkbox("Or whenever I have a raise available", ref anyRaise))
        {
            this.config.JobRuleAnyRaise = anyRaise;
            dirty = true;
        }

        HelpMarker("Covers phantom jobs in the Occult Crescent and anything else that grants a raise without changing your job. Those are not job rows in the game's data, so they cannot appear in the list below.");

        ImGui.Spacing();
        dirty |= this.DrawJobGrid();
        ImGui.Unindent();

        return dirty;
    }

    private bool DrawJobGrid()
    {
        var dirty = false;

        var sheet = Service.Data.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();
        if (sheet == null)
        {
            TextHint("Job list unavailable.");
            return dirty;
        }

        var column = 0;
        foreach (var job in sheet)
        {
            // Rows below 19 are the base classes, which you cannot be in current
            // content, and role 0 is everything that is not a combat job.
            if (job.RowId < 19 || job.Role == 0)
                continue;

            var abbreviation = job.Abbreviation.ExtractText();
            if (string.IsNullOrWhiteSpace(abbreviation))
                continue;

            if (column % 6 != 0)
                ImGui.SameLine();
            column++;

            var ticked = this.config.VisibleJobs.Contains(job.RowId);
            if (ImGui.Checkbox($"{abbreviation}##job{job.RowId}", ref ticked))
            {
                if (ticked)
                    this.config.VisibleJobs.Add(job.RowId);
                else
                    this.config.VisibleJobs.Remove(job.RowId);

                dirty = true;
            }
        }

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

        HelpMarker(this.config.Orientation == ListOrientation.Vertical
            ? "These pin one corner of the list; the first player sits in it and later ones fill away from it. Vertical lists add new columns, so the horizontal setting is the one that matters most."
            : "These pin one corner of the list; the first player sits in it and later ones fill away from it. Horizontal lists add new rows, so the vertical setting is the one that matters most.");

        ImGui.Separator();

        var showHp = this.config.ShowHpNumbers;
        if (ImGui.Checkbox("Show HP numbers instead of a percentage", ref showHp))
        {
            this.config.ShowHpNumbers = showHp;
            dirty = true;
        }

        HelpMarker("Changes the label written on top of each HP bar, from 75% to 12,345 / 16,000. The bar itself is unaffected, and dead players show their raise state either way.");

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

        if ((SortMode)sort == SortMode.MissingHp)
        {
            ImGui.Indent();
            ImGui.SetNextItemWidth(220);
            var tie = (int)this.config.MissingHpTieBreak;
            if (ImGui.Combo("Then by", ref tie, "Role\0Alphabetically\0"))
            {
                this.config.MissingHpTieBreak = (TieBreak)tie;
                dirty = true;
            }

            HelpMarker("Players at the same health percentage are common, especially when everyone is at full. Without a tie-break their order is arbitrary and can reshuffle on its own.");
            ImGui.Unindent();
        }

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
                HelpMarker("You count as party for this, including when solo.");

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

        dirty |= DrawHighlight("Highlight your target", ref this.config.HighlightTarget, ref this.config.TargetColor, "target");
        HelpMarker("Shows which player you already have selected. The colour is drawn as a border around the box, and optionally blended into its background.");
        dirty |= DrawHighlight("Highlight your soft target", ref this.config.HighlightSoftTarget, ref this.config.SoftTargetColor, "soft");
        dirty |= DrawHighlight("Highlight your focus target", ref this.config.HighlightFocusTarget, ref this.config.FocusTargetColor, "focus");
        dirty |= DrawHighlight("Highlight party members", ref this.config.HighlightParty, ref this.config.PartyColor, "party");

        var tint = this.config.HighlightTintBackground;
        if (ImGui.Checkbox("Also tint the box background", ref tint))
        {
            this.config.HighlightTintBackground = tint;
            dirty = true;
        }

        HelpMarker("If a player is both your soft target and your hard target, the soft target colour is drawn outside and the target colour inside.");

        ImGui.Separator();

        if (ImGui.BeginTable("##npl_clicks", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Click", ImGuiTableColumnFlags.WidthFixed, 130f);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 160f);
            ImGui.TableSetupColumn("Raise if dead", ImGuiTableColumnFlags.WidthFixed, 100f);
            ImGui.TableHeadersRow();

            dirty |= this.DrawBinding("Left click", ref this.config.LeftClick, ref this.config.LeftClickRaise);
            dirty |= this.DrawBinding("Right click", ref this.config.RightClick, ref this.config.RightClickRaise);
            dirty |= this.DrawBinding("Middle click", ref this.config.MiddleClick, ref this.config.MiddleClickRaise);
            dirty |= this.DrawBinding("Ctrl + left click", ref this.config.CtrlLeftClick, ref this.config.CtrlLeftClickRaise);
            dirty |= this.DrawBinding("Ctrl + right click", ref this.config.CtrlRightClick, ref this.config.CtrlRightClickRaise);
            dirty |= this.DrawBinding("Alt + left click", ref this.config.AltLeftClick, ref this.config.AltLeftClickRaise);
            dirty |= this.DrawBinding("Alt + right click", ref this.config.AltRightClick, ref this.config.AltRightClickRaise);

            ImGui.EndTable();
        }

        TextHint("With Raise if dead ticked, that click tries the raise first and falls back to its own action.");
        HelpMarker("Each click keeps its own fallback. Left click can raise then hard target, while right click raises then soft targets. The fallback is used when the player is alive, or when your job has no raise available.");

        ImGui.Separator();

        var showRaise = this.config.ShowRaiseInProgress;
        if (ImGui.Checkbox("Show when a player is being raised by someone else", ref showRaise))
        {
            this.config.ShowRaiseInProgress = showRaise;
            dirty = true;
        }

        var swift = this.config.UseSwiftcast;
        if (ImGui.Checkbox("Use Swiftcast when the raise has a cast time", ref swift))
        {
            this.config.UseSwiftcast = swift;
            dirty = true;
        }

        HelpMarker("Raising from the list targets the player, uses Swiftcast if the raise has a cast time and Swiftcast is up, then casts the raise once the instant cast lands. Swiftcast is skipped when an instant cast is already active, such as a Red Mage mid-Dualcast, and when the raise is already instant. The raise used is whichever one your job currently has available, so phantom job raises work too.");

        var ready = this.config.ShowRaiseReady;
        if (ImGui.Checkbox("Show \"Raise ready\" instead of \"Dead\"", ref ready))
        {
            this.config.ShowRaiseReady = ready;
            dirty = true;
        }

        HelpMarker("Tells you before you click whether the raise would go off instantly, rather than silently starting a long hard cast.");
        ImGui.Separator();

        TextHint("Found a bug, or have an idea? Open an issue:");

        if (ImGui.Button("Open the issue tracker"))
            Dalamud.Utility.Util.OpenLink(IssueUrl);

        ImGui.SameLine();
        if (ImGui.Button("Copy link"))
            ImGui.SetClipboardText(IssueUrl);

        TextHint(IssueUrl);

        return dirty;
    }

    // A dim (?) that reveals its explanation on hover, so the settings stay a scannable
    // list of controls instead of a wall of prose.
    private static void HelpMarker(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24f);
        ImGui.TextWrapped(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    // ImGui.TextDisabled does not wrap.
    private static void TextHint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private bool DrawBinding(string label, ref ClickAction action, ref bool raiseFirst)
    {
        var changed = false;

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var value = (int)action;
        if (ImGui.Combo($"##act_{label}", ref value, "Do nothing\0Target\0Soft target\0Focus target\0"))
        {
            action = (ClickAction)value;
            changed = true;
        }

        ImGui.TableNextColumn();
        var raise = raiseFirst;
        if (ImGui.Checkbox($"##raise_{label}", ref raise))
        {
            raiseFirst = raise;
            changed = true;
        }

        return changed;
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































