using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>
/// A wall split around its openings leaves flat pieces whose edges do not match one to one (T-junctions). The seams
/// between those pieces must not be drawn as lines, while the real edges of the openings must stay.
/// </summary>
static class FeatureEdgeSeamChecks
{
    public static void Run(Action<bool, string> check)
    {
        CheckSyntheticWall(check);
        CheckDemonstrationHouse(check);
    }

    private static Vector3 P(float x, float z) => new(x, 0, z);

    /// <summary>A 6 m wall with a window hole between x 2..4 and z 1..2, built from five quads like the mesher does.</summary>
    private static void CheckSyntheticWall(Action<bool, string> check)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        void Quad(float x1, float z1, float x2, float z2)
        {
            var first = positions.Count;
            positions.AddRange([P(x1, z1), P(x2, z1), P(x2, z2), P(x1, z2)]);
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }
        Quad(0, 0, 2, 3);   // left of the window, full height
        Quad(2, 0, 4, 1);   // below the window
        Quad(2, 2, 4, 3);   // above the window
        Quad(4, 0, 6, 3);   // right of the window

        var edges = FeatureEdges.Extract(positions, indices);
        bool OnVertical(EdgeSegment e, float x) => MathF.Abs(e.A.X - x) < 1e-3f && MathF.Abs(e.B.X - x) < 1e-3f;
        float Middle(EdgeSegment e) => (e.A.Z + e.B.Z) / 2;

        foreach (var x in new[] { 2f, 4f })
        {
            check(!edges.Any(e => OnVertical(e, x) && (Middle(e) < 1 || Middle(e) > 2)),
                $"No seam line below or above the window at x = {x}");
            check(edges.Any(e => OnVertical(e, x) && Middle(e) > 1 && Middle(e) < 2),
                $"The window jamb at x = {x} is still drawn");
        }
        check(edges.Any(e => MathF.Abs(e.A.Z - 1) < 1e-3f && MathF.Abs(e.B.Z - 1) < 1e-3f && MathF.Abs((e.A.X + e.B.X) / 2 - 3) < 0.6f) &&
              edges.Any(e => MathF.Abs(e.A.Z - 2) < 1e-3f && MathF.Abs(e.B.Z - 2) < 1e-3f && MathF.Abs((e.A.X + e.B.X) / 2 - 3) < 0.6f),
            "The window sill and head are still drawn");
        check(edges.Any(e => MathF.Abs(e.A.Z) < 1e-3f && MathF.Abs(e.B.Z) < 1e-3f) &&
              edges.Any(e => OnVertical(e, 0)) && edges.Any(e => OnVertical(e, 6)),
            "The outer boundary of the wall is still drawn");
    }

    /// <summary>The demonstration house's south wall: its window and door sides must not leave vertical lines on the plain wall.</summary>
    private static void CheckDemonstrationHouse(Action<bool, string> check)
    {
        var document = SampleProjects.CreateDemonstrationHouse();
        var mesh = BuildingMesher.Build(document);
        var edges = FeatureEdges.Extract(mesh.Positions, mesh.Indices);
        var south = document.Walls.First(wall => wall.Start.Y == 0 && wall.End.Y == 0 && wall.Start.X < wall.End.X);
        var face = (float)(south.Start.Y - south.Thickness / 2);

        var stray = 0;
        foreach (var window in document.Openings.Where(o => o.WallId == south.Id && o.Kind == OpeningKind.Window))
        {
            foreach (var x in new[] { (float)(south.Start.X + window.Begin), (float)(south.Start.X + window.End) })
                stray += edges.Count(e => MathF.Abs(e.A.Y - face) < 1e-3f && MathF.Abs(e.B.Y - face) < 1e-3f &&
                    MathF.Abs(e.A.X - x) < 1e-3f && MathF.Abs(e.B.X - x) < 1e-3f &&
                    ((e.A.Z + e.B.Z) / 2 < window.Sill - 0.01f || (e.A.Z + e.B.Z) / 2 > window.Sill + window.Height + 0.01f));
        }
        check(stray == 0, $"No vertical seam lines run above or below the south windows ({stray} found)");

        // Along the top of the wall, the pieces left and right of a window meet in a short transverse seam.
        var top = (float)south.Height;
        var transverse = 0;
        foreach (var window in document.Openings.Where(o => o.WallId == south.Id))
            foreach (var x in new[] { (float)(south.Start.X + window.Begin), (float)(south.Start.X + window.End) })
                transverse += edges.Count(e => MathF.Abs(e.A.Z - top) < 1e-3f && MathF.Abs(e.B.Z - top) < 1e-3f &&
                    MathF.Abs(e.A.X - x) < 1e-3f && MathF.Abs(e.B.X - x) < 1e-3f);
        check(transverse == 0, $"No transverse seam lines cross the top of the wall at its openings ({transverse} found)");
    }
}
