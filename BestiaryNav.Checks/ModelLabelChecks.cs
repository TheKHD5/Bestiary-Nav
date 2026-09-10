using System;
using System.Numerics;
using BestiaryNav;

static class ModelLabelChecks
{
    public static void Run(Action<bool, string> check)
    {
        var viewport = new Vector2(100, 50);
        var size = new Vector2(800, 600);
        var textSize = new Vector2(120, 20);
        foreach (var anchor in new[] { new Vector2(101, 51), new Vector2(899, 51),
            new Vector2(101, 649), new Vector2(899, 649), new Vector2(500, 350) })
        {
            check(ModelLabelLayout.TryPlace(anchor, textSize, viewport, size, out var text, out var tip), "model label fits viewport");
            check(text.X - 5 >= viewport.X + 4 && text.Y - 3 >= viewport.Y + 4 &&
                text.X + textSize.X + 5 <= viewport.X + size.X - 4 &&
                text.Y + textSize.Y + 3 <= viewport.Y + size.Y - 4 &&
                tip.X - 6 >= viewport.X + 4 && tip.X + 6 <= viewport.X + size.X - 4 &&
                tip.Y - 9 >= viewport.Y + 4 && tip.Y <= viewport.Y + size.Y - 4,
                "full label background and arrow remain inside screen at every edge");
        }
        check(!ModelLabelLayout.TryPlace(new(float.NaN, 20), textSize, viewport, size, out _, out _), "invalid projection rejected");
        check(!ModelLabelLayout.TryPlace(new(400, 300), textSize, viewport, new(100, 30), out _, out _), "label too large is not drawn outside viewport");

        // Live Ichorous Ire snapshot, 2026-09-11: radius 3.5, world Y -38.
        // Simulate zooming close enough that the upper anchor is above the camera view.
        var position = new Vector3(21.957703f, -38, 114.70203f);
        var calls = 0;
        bool BodyOnly(Vector3 world, out Vector2 screen)
        {
            calls++;
            screen = new(400, world.Y > -35 ? -100 : 100);
            return screen.Y >= 0;
        }
        check(ModelLabelLayout.TryProject(position, 3.5f, BodyOnly, out var anchorPoint) &&
            anchorPoint == new Vector2(400, 100) && calls == 2, "visible boss body keeps marker when raised anchor leaves screen");
        calls = 0;
        bool FeetOnly(Vector3 world, out Vector2 screen)
        {
            calls++;
            screen = new(400, world.Y <= -37 ? 100 : -100);
            return screen.Y >= 0;
        }
        check(ModelLabelLayout.TryProject(position, 3.5f, FeetOnly, out _) && calls == 3, "lower body fallback remains visible at close zoom");
        calls = 0;
        bool Hidden(Vector3 world, out Vector2 screen) { calls++; screen = default; return false; }
        check(!ModelLabelLayout.TryProject(position, 3.5f, Hidden, out _) && calls == 3, "fully offscreen or behind-camera boss has no marker");
        calls = 0;
        bool Visible(Vector3 world, out Vector2 screen) { calls++; screen = new(400, 100); return true; }
        check(ModelLabelLayout.TryProject(position, 3.5f, Visible, out _) && calls == 1, "normal projection needs one call");
        check(!ModelLabelLayout.TryProject(position, float.NaN, Visible, out _), "invalid radius rejected");
    }
}
