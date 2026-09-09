using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace NearbyPlayerList;

// Decides whether the list should exist at all right now, and which filter mode
// applies. Both the window and the scanner consult this, so the two cannot disagree.
public enum ListVisibility
{
    Visible,

    // A zone or job rule is suppressing the list. The button strip may still be shown
    // so the settings stay reachable and the state is visible.
    HiddenByRule,

    // Nothing renders at all. Currently only PvP.
    Blocked,
}

public static unsafe class VisibilityResolver
{
    private static DateTime lastRaiseCheck = DateTime.MinValue;
    private static bool cachedRaiseAvailable;

    public static bool ShouldShow(Configuration config, IReadOnlyCollection<uint> raiseActions)
        => Evaluate(config, raiseActions, out _) == ListVisibility.Visible;

    public static ListVisibility Evaluate(Configuration config, IReadOnlyCollection<uint> raiseActions, out string reason)
    {
        reason = string.Empty;
        // Hard off in PvP, and deliberately not a setting. The list is targetable
        // players with live HP and one-click targeting, which in PvP is an enemy list
        // with a target assist. IClientState.IsPvP is used rather than a hand-kept list
        // of zone ids, which would go stale and fail open.
        if (Service.ClientState.IsPvP)
        {
            reason = "Disabled in PvP.";
            return ListVisibility.Blocked;
        }

        if (config.UseZoneRules)
        {
            var zone = ZoneClassifier.Current();
            if (config.ZoneRules.TryGetValue(zone, out var rule))
            {
                if (rule.Visibility == ZoneVisibility.Hide)
                {
                    reason = $"Hidden here by the {ZoneClassifier.Label(zone)} zone rule.";
                    return ListVisibility.HiddenByRule;
                }

                // An explicit Show for this zone overrides the job rule.
                if (rule.Visibility == ZoneVisibility.Show)
                    return ListVisibility.Visible;
            }
        }

        if (config.UseJobRule && !JobAllows(config, raiseActions))
        {
            reason = "Hidden on this job by the job rule.";
            return ListVisibility.HiddenByRule;
        }

        return ListVisibility.Visible;
    }

    private static bool JobAllows(Configuration config, IReadOnlyCollection<uint> raiseActions)
    {
        var local = Service.Objects.LocalPlayer;
        if (local == null)
            return false;

        if (config.VisibleJobs.Contains(local.ClassJob.RowId))
            return true;

        return config.JobRuleAnyRaise && HasRaiseAvailable(raiseActions);
    }

    // Covers phantom jobs and anything else that grants a raise without changing your
    // ClassJob, which a job checklist structurally cannot express. Throttled because it
    // asks the game about every known raise action.
    private static bool HasRaiseAvailable(IReadOnlyCollection<uint> raiseActions)
    {
        var now = DateTime.UtcNow;
        if ((now - lastRaiseCheck).TotalMilliseconds < 1000)
            return cachedRaiseAvailable;

        lastRaiseCheck = now;
        cachedRaiseAvailable = false;

        try
        {
            var manager = ActionManager.Instance();
            if (manager == null)
                return false;

            foreach (var id in raiseActions)
            {
                if (manager->GetActionStatus(ActionType.Action, id) == 0)
                {
                    cachedRaiseAvailable = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Could not check raise availability; assuming none.");
        }

        return cachedRaiseAvailable;
    }

    // Diagnostic for /npl raisedebug. Prints what the game says about every action the
    // plugin treats as a raise, so a false positive can be identified rather than
    // guessed at.
    public static void DumpRaiseStatus(IReadOnlyCollection<uint> raiseActions)
    {
        var local = Service.Objects.LocalPlayer;
        Service.Log.Information($"[raisedebug] job={local?.ClassJob.RowId}, actions={raiseActions.Count}");

        // Printed with IChatGui, which writes to your own log only. That is useless for
        // announcing a raise to other players, but exactly right for a diagnostic.
        var usable = new List<string>();

        ActionManager* manager;
        try
        {
            manager = ActionManager.Instance();
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "[raisedebug] no ActionManager");
            return;
        }

        if (manager == null)
        {
            Service.Log.Information("[raisedebug] ActionManager was null");
            return;
        }

        var sheet = Service.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();

        foreach (var id in raiseActions)
        {
            uint status;
            try
            {
                status = manager->GetActionStatus(ActionType.Action, id);
            }
            catch (Exception ex)
            {
                Service.Log.Information($"[raisedebug] {id}: threw {ex.GetType().Name}");
                continue;
            }

            var row = sheet?.GetRowOrDefault(id);
            var name = row?.Name.ExtractText() ?? "?";
            var isPlayer = row?.IsPlayerAction;
            var job = row?.ClassJob.RowId;

            Service.Log.Information(
                $"[raisedebug] {id,6} status={status,5} player={isPlayer} job={job,3}  {name}");

            if (status == 0)
                usable.Add($"{name} ({id})");
        }

        ReportRaiseStatus(local?.ClassJob.RowId, raiseActions.Count, usable);
    }

    private static void ReportRaiseStatus(uint? job, int checkedCount, List<string> usable)
    {
        Service.Chat.Print($"[NPL] job {job}, checked {checkedCount} raise actions.");

        if (usable.Count == 0)
        {
            Service.Chat.Print("[NPL] none available. The job rule would hide the list.");
            return;
        }

        Service.Chat.Print($"[NPL] available: {string.Join(", ", usable)}");
    }

    // A manual filter click beats the zone rule until you leave the zone. Kept in
    // memory rather than saved, so it never quietly rewrites the configured mode.
    private static uint overrideTerritory = uint.MaxValue;
    private static FilterMode? sessionOverride;

    public static void SetManualFilter(FilterMode mode)
    {
        overrideTerritory = Service.ClientState.TerritoryType;
        sessionOverride = mode;
    }

    public static bool ZoneOverridesFilter(Configuration config)
    {
        if (!config.UseZoneRules)
            return false;

        return config.ZoneRules.TryGetValue(ZoneClassifier.Current(), out var rule)
               && rule.Filter != ZoneFilterOverride.NoChange;
    }

    public static FilterMode EffectiveFilter(Configuration config)
    {
        if (sessionOverride.HasValue)
        {
            if (overrideTerritory == Service.ClientState.TerritoryType)
                return sessionOverride.Value;

            sessionOverride = null;
        }

        if (!config.UseZoneRules)
            return config.Filter;

        if (!config.ZoneRules.TryGetValue(ZoneClassifier.Current(), out var rule))
            return config.Filter;

        return rule.Filter switch
        {
            ZoneFilterOverride.All => FilterMode.All,
            ZoneFilterOverride.BelowThreshold => FilterMode.BelowHealthThreshold,
            ZoneFilterOverride.DeadOnly => FilterMode.DeadOnly,
            _ => config.Filter,
        };
    }
}




