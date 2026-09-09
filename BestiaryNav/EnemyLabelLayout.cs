using System;
using System.Numerics;

namespace BestiaryNav;

internal readonly record struct EnemyRowBounds(float Left, float Right, float CenterY);

internal static class EnemyLabelLayout
{
    public static bool TryPlace(EnemyRowBounds row, Vector2 textSize, Vector2 viewportOrigin,
        Vector2 viewportSize, out Vector2 position)
    {
        position = default;
        if (!float.IsFinite(row.Left) || !float.IsFinite(row.Right) || !float.IsFinite(row.CenterY) ||
            row.Right < row.Left || !Finite(textSize) || !Finite(viewportOrigin) || !Finite(viewportSize) ||
            textSize.X <= 0 || textSize.Y <= 0)
            return false;
        // Include the label's background padding and a margin inside the viewport.
        var min = viewportOrigin + new Vector2(9, 7);
        var max = viewportOrigin + viewportSize - textSize - new Vector2(9, 7);
        if (max.X < min.X || max.Y < min.Y)
            return false;
        var x = viewportOrigin.X + row.Right + 13;
        if (x > max.X)
            x = viewportOrigin.X + row.Left - textSize.X - 13;
        var y = viewportOrigin.Y + row.CenterY - textSize.Y / 2;
        position = Vector2.Clamp(new Vector2(x, y), min, max);
        return true;
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
