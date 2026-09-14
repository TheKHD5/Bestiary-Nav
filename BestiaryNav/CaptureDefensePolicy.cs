using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BestiaryNav;

internal readonly record struct CaptureAggressor(ulong Id, ulong Target, Vector3 Position, bool Alive, bool Targetable, bool Engaged);

internal static class CaptureDefensePolicy
{
    public static bool AttackingPlayer(CaptureAggressor enemy, ulong player) =>
        player is not (0 or 0xE0000000) && enemy.Id is not (0 or 0xE0000000) &&
        enemy.Id != player && enemy.Target == player && enemy.Alive && enemy.Targetable && enemy.Engaged;

    public static bool AttackingPlayerOrCompanion(CaptureAggressor enemy, ulong player, ulong companion) =>
        player is not (0 or 0xE0000000) && enemy.Id != player && enemy.Id != companion &&
        (AttackingPlayer(enemy, player) || AttackingPlayer(enemy, companion));

    public static ulong? Select(IEnumerable<CaptureAggressor> enemies, ulong player, Vector3 position, ulong preferred = 0, ulong exclude = 0, ulong companion = 0) =>
        enemies.Where(e => e.Id != exclude && AttackingPlayerOrCompanion(e, player, companion) && Vector3.DistanceSquared(e.Position, position) <= 60 * 60)
            .OrderBy(e => e.Id == preferred ? 0 : 1).ThenBy(e => Vector3.DistanceSquared(e.Position, position))
            .Select(e => (ulong?)e.Id).FirstOrDefault();
}
