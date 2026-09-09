using System;
using Dalamud.Configuration;

namespace BestiaryNav;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool EnableClickNavigation { get; set; } = true;
    public bool BlockInCombat { get; set; } = true;
    public bool ShowUncapturedMarkers { get; set; }
    public float MarkerRange { get; set; } = 50;
    public bool AutoTravel { get; set; }
    public bool CancelTravelOnManualMovement { get; set; }
}
