using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace NearbyPlayerList;

// Decides whether the list should exist at all right now, and which filter mode
// applies. Both the window and the scanner consult this, so the two cannot disagree.
public static unsafe class VisibilityResolver
{
    private static DateTime lastRaiseCheck = DateTime.MinValue;
    private static bool cachedRaiseAvailable;

    public static bool ShouldShow(Configuration config, IReadOnlyCollection<uint> raiseActions)
    {
        // Hard off in PvP, and deliberately not a setting. The list is targetable
        // players with live HP and one-click targeting, which in PvP is an enemy list
        // with a target assist. IClientState.IsPvP is used rather than a hand-kept list
        // of zone ids, which would go stale and fail open.
        if (Service.ClientState.IsPvP)
            return false;

        var zone = ZoneCategory.Other;
        if (config.UseZoneRules)
        {
            zone = ZoneClassifier.Current();
            if (config.ZoneRules.TryGetValue(zone, out var rule))
            {
                if (rule.Visibility == ZoneVisibility.Hide)
                    return false;

                // An explicit Show for this zone overrides the job rule.
                if (rule.Visibility == ZoneVisibility.Show)
                    return true;
            }
        }

        return !config.UseJobRule || JobAllows(config, raiseActions);
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

    public static FilterMode EffectiveFilter(Configuration config)
    {
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
