using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using BestiaryNav;

internal static class CollectionChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject,
        IReadOnlyDictionary<uint, MonsterEntry> catalog)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var metadata = JsonSerializer.Deserialize<CollectionMetadata>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "collection.json")), options)!;
        var index = metadata.BuildIndex(catalog);
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sources", "acquisition.json")));
        foreach (var row in source.RootElement.GetProperty("beasts").EnumerateArray())
        {
            var entry = index[row.GetProperty("bestiaryNumber").GetUInt32()];
            check(entry.MinimumLevel == row.GetProperty("minimumLevel").GetInt32(), "planning level matches acquisition source");
            check(entry.Source == row.GetProperty("beastSource").GetString(), "planning provenance retained");
        }
        reject(() => new CollectionMetadata().BuildIndex(catalog), "incomplete planning data rejected");
        metadata.Beasts.Add(metadata.Beasts[0]);
        reject(() => metadata.BuildIndex(catalog), "duplicate planning data rejected");
        metadata.Beasts.RemoveAt(metadata.Beasts.Count - 1);
        metadata.Beasts[0].MinimumLevel = 0;
        reject(() => metadata.BuildIndex(catalog), "unknown minimum level rejected");

        MapLocation point = new() { X = 21.5f, Y = 21.5f };
        check(SpawnAreaCoordinates.Convert(point, 100, 0, 0, 60) == new SpawnAreaCenter(0, 0, 60), "map center becomes native world center");
        check(SpawnAreaCoordinates.Convert(point, 100, 100, -100, 10) == new SpawnAreaCenter(-100, 100, 10), "circle offsets have correct signs");
        point.X = point.Y = 11.25f;
        check(SpawnAreaCoordinates.Convert(point, 200, 0, 0, 200) == new SpawnAreaCenter(0, 0, 200), "scaled map circle preserves world radius");
        point.X = point.Y = 1;
        check(SpawnAreaCoordinates.Convert(point, 100, 0, 0, 60).X == -1024, "map boundary uses world units without thousandfold scaling");
        foreach (var radius in new[] { float.NaN, float.PositiveInfinity, 0, 9, 201 })
            reject(() => SpawnAreaCoordinates.Convert(point, 100, 0, 0, radius), "invalid circle radius rejected");
        point.X = float.NaN;
        reject(() => SpawnAreaCoordinates.Convert(point, 100, 0, 0, 60), "invalid circle coordinate rejected");
        point.X = 42;
        reject(() => SpawnAreaCoordinates.Convert(point, 200, 0, 0, 60), "circle outside scaled map rejected");

        MonsterEntry Beast(uint number, uint territory, float x) => new()
        {
            BestiaryNumber = number, DisplayName = $"Beast {number}",
            Locations = [new() { TerritoryTypeId = territory, MapId = territory, X = x, Y = 10 }],
        };
        var local = Beast(2, 134, 14);
        var close = Beast(3, 134, 11);
        var remote = Beast(4, 135, 10);
        var overLevel = Beast(5, 134, 10);
        MonsterEntry[] beasts = [local, close, remote, overLevel];
        var levels = beasts.ToDictionary(b => b.BestiaryNumber, b => new BeastAcquisition
            { BestiaryNumber = b.BestiaryNumber, MinimumLevel = b == overLevel ? 11 : 1 });
        var state = new CollectionSnapshot(true, 0, 10, 134, 134, new(10, 10), "ready");
        MonsterEntry? Next(CollectionSnapshot snapshot) => CollectionPlanner.Recommend(beasts, levels, snapshot);
        check(Next(state)?.BestiaryNumber == 3, "nearest level-eligible local beast wins");
        check(Next(state with { Captured = 1UL << 2 })?.BestiaryNumber == 2, "captured beast excluded");
        check(Next(state with { Captured = (1UL << 2) | (1UL << 1) })?.BestiaryNumber == 4, "eligible remote beast beats local above-level beast");
        check(Next(state with { Level = 11 })?.BestiaryNumber == 5, "equal level is eligible");
        check(Next(state with { Ready = false }) == null, "unknown capture records never treated as uncaptured");
        check(Next(state with { Level = 0 }) == null, "no suggestion without player level");
        check(Next(state with { Territory = 0 }) == null, "no suggestion during unavailable territory");
        check(Next(state with { Captured = ulong.MaxValue }) == null, "complete collection yields no suggestion");
        check(Next(state with { Territory = 135, Map = 135 })?.BestiaryNumber == 4, "territory priority updates on travel");
        remote.Locations.Add(new() { TerritoryTypeId = 134, MapId = 134, X = 12, Y = 10 });
        check(CollectionPlanner.PreferredLocation(remote, state)?.TerritoryTypeId == 134, "alternate location in current territory preferred");
        check(CollectionPlanner.AcquisitionLabel(catalog[17]).Contains("gourd", StringComparison.OrdinalIgnoreCase), "duty acquisition identifies gourd encounter");
        check(CollectionPlanner.AcquisitionLabel(catalog[1]) == "Quest reward", "quest acquisition is distinct");
        check(CollectionPlanner.Area(catalog[17]) == "Copperbell Mines", "collection groups duty by exact destination");
    }
}
