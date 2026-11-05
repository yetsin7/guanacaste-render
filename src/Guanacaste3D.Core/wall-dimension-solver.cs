namespace Guanacaste3D.Core;

/// <summary>Dimensions that must not change while an edit solves for the others.</summary>
public readonly record struct WallDimensionLocks(bool Length = false, bool Height = false, bool Thickness = false);

/// <summary>
/// One requested wall edit. Null means "leave as is". <see cref="FaceArea"/> is the
/// vertical face area (length × height); the solver moves whichever of length or
/// height is not fixed so that the face reaches the requested area.
/// </summary>
public readonly record struct WallDimensionEdit(
    double? Length = null,
    double? Height = null,
    double? Thickness = null,
    double? FaceArea = null,
    WallDimensionLocks Locks = default);

/// <summary>Resolves a wall edit into concrete dimensions while honoring locked dimensions.</summary>
public static class WallDimensionSolver
{
    /// <summary>Tolerance below which two dimensions are considered unchanged, in meters or square meters.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>
    /// Returns the wall with the edit applied. An explicit value for a locked
    /// dimension is rejected, and an area request is solved with the dimensions that
    /// remain free. Validation of the resulting wall stays with the document.
    /// </summary>
    public static Wall Resolve(Wall wall, WallDimensionEdit edit)
    {
        var length = wall.Length;
        var height = wall.Height;
        var thickness = wall.Thickness;
        var lengthFixed = edit.Locks.Length;
        var heightFixed = edit.Locks.Height;

        if (edit.Length is double requestedLength && !Same(requestedLength, length))
        {
            if (edit.Locks.Length) throw new InvalidDataException("A locked dimension cannot change.");
            length = requestedLength;
            lengthFixed = true;
        }
        if (edit.Height is double requestedHeight && !Same(requestedHeight, height))
        {
            if (edit.Locks.Height) throw new InvalidDataException("A locked dimension cannot change.");
            height = requestedHeight;
            heightFixed = true;
        }
        if (edit.Thickness is double requestedThickness && !Same(requestedThickness, thickness))
        {
            if (edit.Locks.Thickness) throw new InvalidDataException("A locked dimension cannot change.");
            thickness = requestedThickness;
        }
        if (edit.FaceArea is double area && !Same(area, length * height))
        {
            if (!double.IsFinite(area) || area <= 0)
                throw new InvalidDataException("The requested wall area is invalid.");
            if (lengthFixed && heightFixed)
                throw new InvalidDataException("With length and height both fixed, the area cannot change.");
            if (lengthFixed) height = area / length;
            else if (heightFixed) length = area / height;
            else (length, height) = ResolveFreeArea(length, height, area);
        }

        var direction = wall.Length > 0
            ? new Point2((wall.End.X - wall.Start.X) / wall.Length, (wall.End.Y - wall.Start.Y) / wall.Length)
            : new Point2(1, 0);
        var end = Same(length, wall.Length)
            ? wall.End
            : new Point2(wall.Start.X + direction.X * length, wall.Start.Y + direction.Y * length);
        return wall with { End = end, Height = height, Thickness = thickness };
    }

    /// <summary>
    /// Chooses which free dimension absorbs an area change when neither length nor
    /// height is fixed. The result must keep <c>length × height == area</c>.
    /// </summary>
    private static (double Length, double Height) ResolveFreeArea(double length, double height, double area)
    {
        // TODO(human): choose which dimension absorbs an area change when neither length nor height is locked.
        // Options: keep the height and change the length (current default), keep the length and change
        // the height, or scale both by sqrt(area / (length * height)) to keep their ratio.
        // Return the (Length, Height) pair so that Length * Height == area.
        return (area / height, height);
    }

    /// <summary>Compares two metric values with a tolerance that absorbs floating-point noise.</summary>
    private static bool Same(double a, double b) => Math.Abs(a - b) < Tolerance;
}
