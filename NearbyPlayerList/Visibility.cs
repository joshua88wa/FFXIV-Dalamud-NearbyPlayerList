using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace NearbyPlayerList;

public enum ZoneCategory
{
    Overworld,
    CityInn,
    Housing,
    Dungeon,
    TrialRaid,
    DeepDungeon,
    FieldOperation,
    Other,
}

public enum ZoneVisibility { UseDefault, Show, Hide }

public enum ZoneFilterOverride { NoChange, All, BelowThreshold, DeadOnly }

[Serializable]
public class ZoneRule
{
    public ZoneVisibility Visibility = ZoneVisibility.UseDefault;
    public ZoneFilterOverride Filter = ZoneFilterOverride.NoChange;
}

// Zones are classified from TerritoryType.TerritoryIntendedUse, read off the 7.56
// sheet. The mapping is deliberately not exhaustive: intended use 10 mixes dungeons
// and trials, and new uses appear with each expansion, so anything unrecognised falls
// into Other rather than being guessed at.
public static class ZoneClassifier
{
    private static readonly Dictionary<uint, ZoneCategory> ByIntendedUse = new()
    {
        [1] = ZoneCategory.Overworld,
        [9] = ZoneCategory.Overworld,

        [0] = ZoneCategory.CityInn,
        [2] = ZoneCategory.CityInn,
        [6] = ZoneCategory.CityInn,

        [13] = ZoneCategory.Housing,
        [14] = ZoneCategory.Housing,

        [3] = ZoneCategory.Dungeon,
        [4] = ZoneCategory.Dungeon,

        [7] = ZoneCategory.TrialRaid,
        [8] = ZoneCategory.TrialRaid,
        [12] = ZoneCategory.TrialRaid,
        [16] = ZoneCategory.TrialRaid,
        [17] = ZoneCategory.TrialRaid,
        [57] = ZoneCategory.TrialRaid,
        [58] = ZoneCategory.TrialRaid,

        [31] = ZoneCategory.DeepDungeon,

        [26] = ZoneCategory.FieldOperation,   // Diadem
        [38] = ZoneCategory.FieldOperation,
        [47] = ZoneCategory.FieldOperation,
        [41] = ZoneCategory.FieldOperation,   // Eureka
        [48] = ZoneCategory.FieldOperation,   // Bozja, Zadnor
        [52] = ZoneCategory.FieldOperation,   // Delubrum Reginae
        [53] = ZoneCategory.FieldOperation,
        [61] = ZoneCategory.FieldOperation,   // Occult Crescent
    };

    private static uint cachedTerritory = uint.MaxValue;
    private static ZoneCategory cachedCategory = ZoneCategory.Other;

    public static ZoneCategory Current()
    {
        var territory = Service.ClientState.TerritoryType;
        if (territory == cachedTerritory)
            return cachedCategory;

        cachedTerritory = territory;
        cachedCategory = ZoneCategory.Other;

        try
        {
            var row = Service.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territory);
            if (row != null && ByIntendedUse.TryGetValue(row.Value.TerritoryIntendedUse.RowId, out var category))
                cachedCategory = category;
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Could not classify the current zone.");
        }

        return cachedCategory;
    }

    public static string Label(ZoneCategory category) => category switch
    {
        ZoneCategory.Overworld => "Overworld",
        ZoneCategory.CityInn => "Cities and inns",
        ZoneCategory.Housing => "Housing",
        ZoneCategory.Dungeon => "Dungeons",
        ZoneCategory.TrialRaid => "Trials and raids",
        ZoneCategory.DeepDungeon => "Deep Dungeon",
        ZoneCategory.FieldOperation => "Field Operations",
        _ => "Everything else",
    };
}
