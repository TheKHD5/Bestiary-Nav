using System;
using System.Numerics;

namespace BestiaryNav;

public static class ModelLabelLayout
{
    public delegate bool Project(Vector3 world, out Vector2 screen);

    // A large boss can remain visible while the point above it leaves the screen.
    // Only accept a visible point on the actor; never mark enemies behind the camera.
    public static bool TryProject(Vector3 position, float radius, Project project, out Vector2 anchor)
    {
        anchor = default;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
            !float.IsFinite(radius) || radius < 0)
            return false;
        var height = Math.Clamp(radius * 1.3f, 1.5f, 6f);
        return project(position + new Vector3(0, height, 0), out anchor) ||
            project(position + new Vector3(0, height / 2, 0), out anchor) ||
            project(position + new Vector3(0, 0.5f, 0), out anchor);
    }

    public static bool TryPlace(Vector2 anchor, Vector2 textSize, Vector2 viewportPosition,
        Vector2 viewportSize, out Vector2 textPosition, out Vector2 arrowTip)
    {
        textPosition = arrowTip = default;
        if (!Finite(anchor) || !Finite(textSize) || !Finite(viewportPosition) || !Finite(viewportSize) ||
            textSize.X <= 0 || textSize.Y <= 0)
            return false;
        // Four pixels of margin, the label background, and the arrow beneath it.
        var min = viewportPosition + new Vector2(10, 7);
        var max = viewportPosition + viewportSize - textSize - new Vector2(10, 21);
        if (!Finite(min) || !Finite(max) || max.X < min.X || max.Y < min.Y)
            return false;
        textPosition = Vector2.Clamp(anchor - new Vector2(textSize.X / 2, textSize.Y + 17), min, max);
        arrowTip = new Vector2(Math.Clamp(anchor.X, textPosition.X, textPosition.X + textSize.X),
            textPosition.Y + textSize.Y + 17);
        return true;
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
