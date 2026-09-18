using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BestiaryNav;

var checks = 0;
void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    checks++;
}
void Near(float actual, float expected, string message) => Check(MathF.Abs(actual - expected) < 0.001f, message);
void Reject(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException) { checks++; return; }
    catch (ArgumentOutOfRangeException) { checks++; return; }
    throw new Exception(message);
}

BindingChecks.Run(Check);
await UsageReportingChecks.Run(Check);

// Independent anchors: center and the left/right texture boundaries at two scales.
Near(MapCoordinates.WorldToMap(0, 100, 0), 21.5f, "100% center");
Near(MapCoordinates.WorldToMap(-1024, 100, 0), 1, "100% left");
Near(MapCoordinates.WorldToMap(1024, 100, 0), 42, "100% right");
Near(MapCoordinates.WorldToMap(0, 200, 0), 11.25f, "200% center");
Near(MapCoordinates.WorldToMap(-512, 200, 0), 1, "200% left");
Near(MapCoordinates.WorldToMap(512, 200, 0), 21.5f, "200% right");
Near(MapCoordinates.WorldToMap(-100, 100, 100), 21.5f, "positive offset direction");
Near(MapCoordinates.WorldToMap(100, 100, -100), 21.5f, "negative offset direction");
foreach (ushort scale in new ushort[] { 50, 100, 200, 400 })
foreach (short offset in new short[] { -200, 0, 100 })
foreach (var world in new float[] { -250, 0, 123.456f })
    Near(MapCoordinates.MapToWorld(MapCoordinates.WorldToMap(world, scale, offset), scale, offset), world, "round trip");
Check(!MapCoordinates.IsOnMap(float.NaN, 100), "reject NaN");
Check(!MapCoordinates.IsOnMap(float.PositiveInfinity, 100), "reject infinity");
Check(!MapCoordinates.IsOnMap(22, 200), "respect scale-specific extent");
Check(!MapCoordinates.IsOnMap(0.9f, 100), "reject below texture boundary");
Check(!MapCoordinates.IsOnMap(20, 0), "reject zero scale");
Reject(() => MapCoordinates.WorldToMap(0, 0, 0), "zero scale conversion");
Reject(() => MapCoordinates.MapToWorld(float.NaN, 100, 0), "NaN conversion");

MapLocation Point() => new() { TerritoryTypeId = 123, MapId = 456, X = 18.4f, Y = 27.1f };
MonsterEntry Entry(uint id) => new() { BestiaryNumber = id, DisplayName = "Test beast", Locations = [Point()] };
LocationDatabase Database() => new() { SchemaVersion = 2, Monsters = [Entry(42)] };
Check(Database().BuildIndex().ContainsKey(42), "stable ID lookup");
var empty = new LocationDatabase { SchemaVersion = 2 };
Check(empty.BuildIndex().Count == 0, "unconfigured empty database");
var bad = Database(); bad.SchemaVersion = 1;
Reject(() => bad.BuildIndex(), "schema version");
bad = Database(); bad.Monsters.Add(Entry(42));
Reject(() => bad.BuildIndex(), "duplicate key");
bad = Database(); bad.Monsters[0].BestiaryNumber = 0;
Reject(() => bad.BuildIndex(), "zero monster ID");
bad = Database(); bad.Monsters[0].Locations.Clear();
Reject(() => bad.BuildIndex(), "missing location");
bad = Database(); bad.Monsters[0].Locations[0].X = float.NaN;
Reject(() => bad.BuildIndex(), "nonfinite location");
bad = Database(); bad.Monsters[0].Locations[0].MapId = 0;
Reject(() => bad.BuildIndex(), "zero map ID");
bad = Database(); bad.IdSpace = "unverified";
Reject(() => bad.BuildIndex(), "unknown identity convention");
bad = Database(); bad.Monsters[0].NavigationKind = "duty";
Reject(() => bad.BuildIndex(), "duty cannot silently fall back to map");
bad = Database(); bad.Monsters[0].Locations.Clear(); bad.Monsters[0].NavigationKind = "quest";
Reject(() => bad.BuildIndex(), "quest needs actionable guidance");
bad = Database(); bad.Monsters[0].DisplayName = "";
Reject(() => bad.BuildIndex(), "missing beast name");
var dutyEntry = new MonsterEntry { BestiaryNumber = 17, DisplayName = "Slime", CaptureTarget = "Ichorous Ire",
    NavigationKind = "duty", Duty = new DutyDestination { ContentFinderConditionId = 3, TerritoryTypeId = 1038, Name = "Copperbell Mines" } };
Check(NavigationRequest.ForBeast(dutyEntry) is { Location: null, Duty.ContentFinderConditionId: 3 }, "duty selects CFC ID, not territory");
var questEntry = new MonsterEntry { BestiaryNumber = 1, DisplayName = "Cu Sith", NavigationKind = "quest", AcquisitionNote = "Use quest reward." };
Check(NavigationRequest.ForBeast(questEntry) is { Location: null, Duty: null, Notice.Length: > 0 }, "quest does not create a flag/duty");

// Check the actual shipped roster and its navigation against independent source snapshots.
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var shipped = JsonSerializer.Deserialize<LocationDatabase>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "locations.json")), options)!;
var index = shipped.BuildIndex();
Check(index.Count == 50 && Enumerable.Range(1, 50).All(n => index.ContainsKey((uint)n)), "complete 1..50 catalog");
Check(shipped.Monsters.Count(m => m.NavigationKind == "map") == 37, "37 outdoor destinations");
Check(shipped.Monsters.Count(m => m.NavigationKind == "duty") == 12, "12 duty destinations");
Check(shipped.Monsters.Count(m => m.NavigationKind == "quest") == 1, "one quest acquisition");
using var roster = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sources", "roster.json")));
foreach (var beast in roster.RootElement.GetProperty("beasts").EnumerateArray())
    Check(index[beast.GetProperty("bestiaryNumber").GetUInt32()].DisplayName == beast.GetProperty("displayName").GetString(), "Icy Veins name/number preserved");
using var maps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sources", "map-rows.json")));
using var duties = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sources", "duty-rows.json")));
foreach (var beast in shipped.Monsters)
{
    var request = NavigationRequest.ForBeast(beast);
    Check(beast.Sources.Count >= 3, $"source provenance for #{beast.BestiaryNumber}");
    if (request.Location is { } point)
    {
        var map = maps.RootElement.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("id").GetUInt32() == point.MapId);
        Check(map.GetProperty("territoryTypeId").GetUInt32() == point.TerritoryTypeId, "map belongs to territory");
        var scale = map.GetProperty("sizeFactor").GetUInt16();
        Check(MapCoordinates.IsOnMap(point.X, scale) && MapCoordinates.IsOnMap(point.Y, scale), "coordinates fit actual map scale");
        Check(point.Source.StartsWith("https://"), "point provenance");
    }
    if (request.Duty is { } duty)
    {
        var row = duties.RootElement.GetProperty("duties").EnumerateArray().Single(d => d.GetProperty("id").GetUInt32() == duty.ContentFinderConditionId);
        Check(row.GetProperty("territoryTypeId").GetUInt32() == duty.TerritoryTypeId, "CFC territory matches");
        Check(request.Location == null && beast.Locations.Count == 0, "duty never flags guessed coordinates");
    }
}
Check(index[33].CaptureTarget == "Master Coeurl" && index[33].Locations[0].TerritoryTypeId == 139, "Coeurl targets verified variant/area");
Check(index[41].CaptureTarget == "Black Eft", "Salamander is not matched by generic enemy name");
Check(index[48].Duty!.ContentFinderConditionId == 28, "Karlabos selects Sastasha Hard, not normal");
foreach (var number in Enumerable.Range(1, 50))
    Check(BestiaryEntryLabel.TryParse($"No. {number}", out var parsed) && parsed == number, "live numbered label");
foreach (var label in new[] { "", "???", "No. 0", "No. 51", "Page 2 No. 5", "No. -1", "No. +2", "No. 2 beast", "No. 9999999999999999999999" })
    Check(!BestiaryEntryLabel.TryParse(label, out _), "reject ambiguous/out-of-catalog label");
Check(BestiaryEntryLabel.TryParse(" No. 048 ", out var padded) && padded == 48, "padded label");
Check(BestiaryEntryLabel.TryParse("Nr. 17", out var localized) && localized == 17, "localized prefix");
// Snapshot independently checked against both live Bestiary pages (18 captured).
const ulong captureSnapshot = 0x300102AFBD3;
uint[] capturedNumbers = [1, 2, 5, 7, 8, 9, 10, 12, 13, 14, 15, 16, 18, 20, 22, 29, 41, 42];
foreach (var number in Enumerable.Range(1, 50).Select(x => (uint)x))
    Check(CaptureRules.IsUncaptured(captureSnapshot, number) != capturedNumbers.Contains(number), "capture bit ordering");
Check(!CaptureRules.IsUncaptured(0, 0) && !CaptureRules.IsUncaptured(0, 51), "unknown numbers never marked");
Check(CaptureRules.IsUncaptured(0, 50) && !CaptureRules.IsUncaptured(1UL << 49, 50), "capture immediately removes eligibility");
Check(CaptureRules.IsInRange(2500, 50) && !CaptureRules.IsInRange(2500.1f, 50), "marker distance boundary");
Check(!CaptureRules.IsInRange(float.NaN, 50) && !CaptureRules.IsInRange(1, float.PositiveInfinity), "invalid marker coordinates");
var targetData = JsonSerializer.Deserialize<CaptureTargetDatabase>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "capture-targets.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var targetIndex = targetData.BuildIndex(index);
Check(targetData.Targets.Count == 49 && targetData.Targets.Select(t => t.BestiaryNumber).Distinct().Count() == 49, "all non-quest capture targets");
Check(targetIndex[(134, 392)] == 3, "Lost Lamb in Middle La Noscea maps to Lamb");
Check(targetIndex[(1038, 554)] == 17, "live Copperbell Mines boss maps to Slime");
Check(CaptureRules.IsUncaptured(0x300103EFFFF, 17) && !CaptureRules.IsUncaptured(0x300103FFFFF, 17),
    "Slime is eligible before registration and hidden after registration");
Check(!targetIndex.ContainsKey((148, 392)), "same name outside verified habitat is not assumed capturable");
Check(targetIndex[(387, 3014)] == 48 && !targetIndex.ContainsKey((1036, 3014)), "duty variant restriction");
Check(!targetIndex.Values.Contains(1u), "quest-only beast has no enemy marker");
var badTargets = new CaptureTargetDatabase { Targets = [new CaptureTargetRecord { BestiaryNumber = 2, CaptureTarget = "Wrong", BNpcNameIds = [37] }] };
Reject(() => badTargets.BuildIndex(index), "name mismatch rejected");
TravelChecks.Run(Check);
Check(index[40].Locations[0].TravelFloor is { MinimumY: 25, MaximumY: 27 }, "Ghost retains verified underground navigation floor");
Check(index[31].Locations[0].TravelFloor == null, "Gigantoad keeps ordinary surface and flight navigation");
ChatChecks.Run(Check);
AppearanceChecks.Run(Check);
AutoCaptureChecks.Run(Check);
CaptureRunChecks.Run(Check);
CaptureCombatChecks.Run(Check);
PullHealthChecks.Run(Check);
FarmingRecoveryChecks.Run(Check);
CaptureDefenseChecks.Run(Check);
FarmingChecks.Run(Check);
FarmingPatrolChecks.Run(Check);
CaptureAllChecks.Run(Check);
RotationSolverChecks.Run(Check);
SpawnSearchChecks.Run(Check);
ModelLabelChecks.Run(Check);
CollectionChecks.Run(Check, Reject, index);
Check(MapCoordinates.MarkerToWorld(1255, 100, 0) == 231 && MapCoordinates.MarkerToWorld(767, 100, 0) == -257, "Summerford map marker converts to world X/Z");
Check(MapCoordinates.MarkerToWorld(1024, 200, 100) == -100 && MapCoordinates.MarkerToWorld(1536, 200, 0) == 256, "map marker scale and offset");
Reject(() => MapCoordinates.MarkerToWorld(1024, 0, 0), "zero marker map scale");
Console.WriteLine($"Passed {checks} coordinate, catalog, navigation, entry-label, capture-marker and auto-travel checks.");
