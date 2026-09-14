using System;
using System.Numerics;
using BestiaryNav;

internal static class CaptureDefenseChecks
{
    public static void Run(Action<bool, string> check)
    {
        const ulong player = 0x100000001;
        var enemy = new CaptureAggressor(2, player, new(5, 0, 0), true, true, true);
        check(CaptureDefensePolicy.Select([enemy], player, Vector3.Zero) == 2, "defend against a living direct attacker using the full object ID");
        foreach (var invalid in new[] {
            enemy with { Target = 1 }, enemy with { Target = 0 },
            enemy with { Alive = false }, enemy with { Targetable = false },
            enemy with { Engaged = false }, enemy with { Id = player },
            enemy with { Id = 0 }, enemy with { Id = 0xE0000000 },
            enemy with { Position = new(61, 0, 0) },
            enemy with { Position = new(0, 61, 0) },
            enemy with { Position = new(float.NaN, 0, 0) } })
            check(CaptureDefensePolicy.Select([invalid], player, Vector3.Zero) == null,
                "reject bystanders, another player's enemies, invalid actors, and unreachable scan distances");
        check(CaptureDefensePolicy.Select([enemy], 0, Vector3.Zero) == null, "no defense without a player");
        var closer = enemy with { Id = 3, Position = new(1, 0, 0) };
        check(CaptureDefensePolicy.Select([enemy, closer], player, Vector3.Zero) == 3, "choose nearest direct attacker");
        check(CaptureDefensePolicy.Select([enemy, closer], player, Vector3.Zero, preferred: 2) == 2, "keep fighting the current attacker instead of oscillating");
        check(CaptureDefensePolicy.Select([enemy with { Alive = false }, closer], player, Vector3.Zero, preferred: 2) == 3, "after defender dies select the remaining attacker");
        check(CaptureDefensePolicy.Select([enemy], player, Vector3.Zero, exclude: 2) == null, "own capture target does not interrupt its approach");
        check(CaptureDefensePolicy.Select([enemy, closer], player, Vector3.Zero, exclude: 2) == 3, "an add can interrupt the capture approach");
        check(CaptureDefensePolicy.Select([], player, Vector3.Zero) == null, "combat without a visible attacker never picks a bystander");
    }
}
