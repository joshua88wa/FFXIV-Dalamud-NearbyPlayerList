using System;
using System.Numerics;
using System.Reflection;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace NearbyPlayerList;

public enum ListOrientation { Vertical, Horizontal }
public enum HorizontalGrowth { Right, Left }
public enum VerticalGrowth { Down, Up }
public enum SortMode { Role, Alphabetical, MissingHp }
public enum FilterMode { All, BelowHealthThreshold, DeadOnly }
public enum HideCondition { Never, InCombat, OutOfCombat, WeaponDrawn, WeaponSheathed }
public enum ClickAction { None, HardTarget, SoftTarget, FocusTarget }

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Window
    public bool ShowWindow = true;
    public bool LockPosition = false;
    public float Scale = 1.0f;
    public int MaxPlayers = 0;                 // 0 = unlimited
    public HideCondition HideWhen = HideCondition.Never;

    // Layout
    public ListOrientation Orientation = ListOrientation.Vertical;
    public int LineLength = 8;                 // rows when vertical, columns when horizontal
    public HorizontalGrowth GrowHorizontally = HorizontalGrowth.Right;
    public VerticalGrowth GrowVertically = VerticalGrowth.Down;

    // Filtering
    public FilterMode Filter = FilterMode.All;
    public float HealthThreshold = 0.75f;      // used by BelowHealthThreshold
    public bool FilterIgnoreAlreadyRaised = true;
    public bool HideSelf = false;
    public bool HideParty = false;
    public bool LimitDistance = false;
    public float MaxDistance = 30f;   // roughly the range of most healing and raise spells

    // Sorting
    public SortMode Sort = SortMode.Role;
    public bool PartyAtTop = false;
    public bool SelfAboveParty = false;
    public bool DeadAtTop = false;
    public bool SortIgnoreAlreadyRaised = true;

    // Display
    public bool HighlightTarget = true;
    public bool HighlightSoftTarget = true;
    public bool HighlightFocusTarget = true;
    public bool HighlightParty = true;
    public bool HighlightTintBackground = true;
    public Vector4 TargetColor = new(1.00f, 0.82f, 0.25f, 1f);        // amber
    public Vector4 SoftTargetColor = new(0.35f, 0.85f, 1.00f, 1f);    // cyan
    public Vector4 FocusTargetColor = new(0.80f, 0.45f, 1.00f, 1f);   // violet
    public Vector4 PartyColor = new(0.25f, 0.85f, 0.35f, 1f);         // green

    public bool ShowRaiseInProgress = true;
    public bool ShowHpNumbers = false;
    public ClickAction LeftClick = ClickAction.HardTarget;
    public ClickAction RightClick = ClickAction.SoftTarget;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => this.pluginInterface = pi;

    public void Save() => this.pluginInterface?.SavePluginConfig(this);

    /// <summary>
    /// Copies every saved setting back to its default. Done by reflection over the
    /// public fields rather than by hand, so a setting added later cannot be forgotten
    /// here. The plugin interface field is private and non-serialised, so it is not
    /// touched, and the object identity is preserved because the windows and the
    /// scanner all hold a reference to this same instance.
    /// </summary>
    public void ResetToDefaults()
    {
        var defaults = new Configuration();

        foreach (var field in typeof(Configuration).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsInitOnly || field.IsLiteral)
                continue;

            field.SetValue(this, field.GetValue(defaults));
        }

        this.Save();
    }
}









