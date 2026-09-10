using System;

namespace BestiaryNav;

public readonly record struct SpawnAreaCenter(int X, int Y, int Radius);

public static class SpawnAreaCoordinates
{
    public static SpawnAreaCenter Convert(MapLocation point, ushort size, short offsetX, short offsetY, float radius)
    {
        if (!MapCoordinates.IsOnMap(point.X, size) || !MapCoordinates.IsOnMap(point.Y, size) ||
            !float.IsFinite(radius) || radius is < 10 or > 200)
            throw new InvalidOperationException("Invalid spawn-area coordinates or radius.");
        return new((int)MathF.Round(MapCoordinates.MapToWorld(point.X, size, offsetX)),
            (int)MathF.Round(MapCoordinates.MapToWorld(point.Y, size, offsetY)), (int)MathF.Round(radius));
    }
}
