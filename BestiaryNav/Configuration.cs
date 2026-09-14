using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace BestiaryNav;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public ChatPreferences ChatOutput { get; set; } = new();
    public Appearance Appearance { get; set; } = new();
    public bool AutoCapture { get; set; }
    public int AutoCaptureMaxHpPercent { get; set; } = 100;
    public bool EnableClickNavigation { get; set; } = true;
    // Keep the original serialized name so the saved toggle survives the UI rename.
    public bool MapTrackingOnClick { get; set; } = true;
    public bool BlockInCombat { get; set; } = true;
    public bool ShowUncapturedMarkers { get; set; }
    public bool LabelsOnlyOnBeastmaster { get; set; }
    public float MarkerRange { get; set; } = 50;
    public bool AutoTravel { get; set; }
    public bool CancelTravelOnManualMovement { get; set; }
    public float SpawnAreaRadius { get; set; } = 60;
    public bool ShowModelLabels { get; set; } = true;
    public bool ShowEnemyListLabels { get; set; } = true;
    public float LabelScale { get; set; } = 1;
    public Vector4 EligibleColor { get; set; } = new(0.4f, 0.88f, 0.4f, 1);
    public Vector4 AboveLevelColor { get; set; } = new(1, 0.33f, 0.33f, 1);
    public HashSet<uint> Favorites { get; set; } = [];
}
