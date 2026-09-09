using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace NearbyPlayerList;

public enum ZoneCategory
{
    Overworld,
    CityInn,
    Housing,
    Dungeon,
    TrialRaid,      // retired, kept so older saved configs still deserialise
    DeepDungeon,
    FieldOperation,
    Other,
    Trial,
    Raid,
    AllianceRaid,
}

public enum ZoneVisibility { UseDefault, Show, Hide }

public enum ZoneFilterOverride { NoChange, All, BelowThreshold, DeadOnly }

[Serializable]
public class ZoneRule
{
    public ZoneVisibility Visibility = ZoneVisibility.UseDefault;
    public ZoneFilterOverride Filter = ZoneFilterOverride.NoChange;
}

// Instanced content is classified from ContentFinderCondition.ContentType, which the
// game maintains properly and which separates dungeons, trials and raids. Open zones
// have no ContentFinderCondition row, so those fall back to TerritoryIntendedUse.
//
// TerritoryIntendedUse alone is not enough: use 10 holds both Halatali and the Bowl of
// Embers, so it cannot tell a dungeon from a trial.
public static class ZoneClassifier
{
    private static readonly Dictionary<uint, ZoneCategory> ByContentType = new()
    {
        [2] = ZoneCategory.Dungeon,          // Dungeons
        [30] = ZoneCategory.Dungeon,         // Variant and Criterion
        [4] = ZoneCategory.Trial,            // Trials
        [28] = ZoneCategory.Raid,            // Ultimate
        [37] = ZoneCategory.AllianceRaid,    // Chaotic Alliance Raid
        [21] = ZoneCategory.DeepDungeon,
        [23] = ZoneCategory.FieldOperation,  // Diadem
        [26] = ZoneCategory.FieldOperation,  // Eureka
        [29] = ZoneCategory.FieldOperation,  // Save the Queen, Bozja
        [38] = ZoneCategory.FieldOperation,  // Occult Crescent
    };

    private static readonly Dictionary<uint, ZoneCategory> ByIntendedUse = new()
    {
        [1] = ZoneCategory.Overworld,
        [9] = ZoneCategory.Overworld,
        [0] = ZoneCategory.CityInn,
        [2] = ZoneCategory.CityInn,
        [6] = ZoneCategory.CityInn,
        [13] = ZoneCategory.Housing,
        [14] = ZoneCategory.Housing,
    };

    // Content member type 4 is the 24 man alliance layout, which is what separates a
    // full alliance raid from an 8 man one inside ContentType 5.
    private const uint AllianceMemberType = 4;

    private static uint cachedTerritory = uint.MaxValue;
    private static ZoneCategory cachedCategory = ZoneCategory.Other;

    public static ZoneCategory Current()
    {
        var territory = Service.ClientState.TerritoryType;
        if (territory == cachedTerritory)
            return cachedCategory;

        cachedTerritory = territory;
        cachedCategory = Classify(territory);
        return cachedCategory;
    }

    private static ZoneCategory Classify(uint territory)
    {
        try
        {
            var conditions = Service.Data.GetExcelSheet<ContentFinderCondition>();
            var match = conditions?.FirstOrDefault(c => c.TerritoryType.RowId == territory);

            if (match is { RowId: not 0 })
            {
                var contentType = match.Value.ContentType.RowId;

                if (contentType == 5)
                {
                    return match.Value.ContentMemberType.RowId == AllianceMemberType
                        ? ZoneCategory.AllianceRaid
                        : ZoneCategory.Raid;
                }

                if (ByContentType.TryGetValue(contentType, out var fromContent))
                    return fromContent;
            }

            var row = Service.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territory);
            if (row != null && ByIntendedUse.TryGetValue(row.Value.TerritoryIntendedUse.RowId, out var fromUse))
                return fromUse;
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Could not classify the current zone.");
        }

        return ZoneCategory.Other;
    }

    // TrialRaid is excluded: it only exists so older saved configs still load.
    public static IEnumerable<ZoneCategory> Configurable()
    {
        yield return ZoneCategory.Overworld;
        yield return ZoneCategory.CityInn;
        yield return ZoneCategory.Housing;
        yield return ZoneCategory.Dungeon;
        yield return ZoneCategory.Trial;
        yield return ZoneCategory.Raid;
        yield return ZoneCategory.AllianceRaid;
        yield return ZoneCategory.DeepDungeon;
        yield return ZoneCategory.FieldOperation;
        yield return ZoneCategory.Other;
    }

    public static string Label(ZoneCategory category) => category switch
    {
        ZoneCategory.Overworld => "Overworld",
        ZoneCategory.CityInn => "Cities and inns",
        ZoneCategory.Housing => "Housing",
        ZoneCategory.Dungeon => "Dungeons",
        ZoneCategory.Trial => "Trials",
        ZoneCategory.Raid => "Raids (8 player)",
        ZoneCategory.AllianceRaid => "Alliance raids (24 player)",
        ZoneCategory.TrialRaid => "Trials and raids",
        ZoneCategory.DeepDungeon => "Deep Dungeon",
        ZoneCategory.FieldOperation => "Field Operations",
        _ => "Everything else",
    };
}
