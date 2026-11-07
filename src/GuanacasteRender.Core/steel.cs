namespace Guanacaste3D.Core;

/// <summary>
/// A rectangle cut out of a member, in the member's own frame, from the top of the member down by <see cref="Depth"/>. For a
/// member drawn between two points, <c>U</c> runs from its start toward its end and <c>V</c> across, positive to the left. For a
/// plate, drawn between two opposite corners, <c>U</c> runs along X and <c>V</c> along Y from the corner with the least of each.
/// </summary>
public sealed record MemberCut(double U0, double U1, double V0, double V1, double Depth)
{
    public bool IsValid => double.IsFinite(U0 + U1 + V0 + V1 + Depth) && U1 - U0 >= 0.001 && V1 - V0 >= 0.001 && Depth is > 0 and <= 100 &&
                           Math.Abs(U0) <= 1000 && Math.Abs(U1) <= 1000 && Math.Abs(V0) <= 1000 && Math.Abs(V1) <= 1000;
}

/// <summary>Builds the pieces that are left of a member once its cuts are taken out.</summary>
public static class MemberCuts
{
    public const int MaximumCuts = 64;

    /// <summary>Plates are drawn between two corners, so their frame follows the axes; every other member's frame follows its own axis.</summary>
    public static bool UsesWorldFrame(StructuralElement element) => element.Kind == StructuralElementKind.Plate;

    /// <summary>The frame cuts are measured in: its origin, the direction of U and the direction of V.</summary>
    public static (Point2 Origin, Point2 U, Point2 V) Frame(StructuralElement element)
    {
        if (UsesWorldFrame(element))
        {
            var corners = element.Footprint;
            return (new Point2(corners.Min(p => p.X), corners.Min(p => p.Y)), new Point2(1, 0), new Point2(0, 1));
        }
        var length = element.Length;
        var u = length < 1e-9 ? new Point2(1, 0) : new Point2((element.End.X - element.Start.X) / length, (element.End.Y - element.Start.Y) / length);
        return (element.Start, u, new Point2(-u.Y, u.X));
    }

    private static Point2 At(Point2 origin, Point2 u, Point2 v, double a, double b) =>
        new(origin.X + u.X * a + v.X * b, origin.Y + u.Y * a + v.Y * b);

    /// <summary>The pieces of a member with its cuts taken out: its parts are cut into cells and each cell keeps what lies below the deepest cut over it.</summary>
    public static IEnumerable<ElementPart> Apply(StructuralElement element, IEnumerable<ElementPart> parts, IReadOnlyList<MemberCut> cuts)
    {
        var (origin, u, v) = Frame(element);
        var memberTop = element.Offset + element.Height;
        foreach (var part in parts)
        {
            if (part.EndTop is not null || part.Footprint.Count < 3) { yield return part; continue; }
            var us = part.Footprint.Select(p => (p.X - origin.X) * u.X + (p.Y - origin.Y) * u.Y).ToArray();
            var vs = part.Footprint.Select(p => (p.X - origin.X) * v.X + (p.Y - origin.Y) * v.Y).ToArray();
            double u0 = us.Min(), u1 = us.Max(), v0 = vs.Min(), v1 = vs.Max();
            var uBreaks = new SortedSet<double> { u0, u1 };
            var vBreaks = new SortedSet<double> { v0, v1 };
            foreach (var cut in cuts)
            {
                if (cut.U0 > u0 + 1e-9 && cut.U0 < u1 - 1e-9) uBreaks.Add(cut.U0);
                if (cut.U1 > u0 + 1e-9 && cut.U1 < u1 - 1e-9) uBreaks.Add(cut.U1);
                if (cut.V0 > v0 + 1e-9 && cut.V0 < v1 - 1e-9) vBreaks.Add(cut.V0);
                if (cut.V1 > v0 + 1e-9 && cut.V1 < v1 - 1e-9) vBreaks.Add(cut.V1);
            }
            var uList = uBreaks.ToArray();
            var vList = vBreaks.ToArray();
            for (var i = 0; i + 1 < uList.Length; i++)
            {
                // Neighbouring cells across the member that end at the same height are one piece.
                double? runTop = null;
                var runStart = 0.0;
                for (var j = 0; j <= vList.Length - 1; j++)
                {
                    double? top = null;
                    if (j + 1 < vList.Length)
                    {
                        var centreU = (uList[i] + uList[i + 1]) / 2;
                        var centreV = (vList[j] + vList[j + 1]) / 2;
                        var depth = cuts.Where(cut => centreU >= cut.U0 && centreU <= cut.U1 && centreV >= cut.V0 && centreV <= cut.V1).Select(cut => cut.Depth).DefaultIfEmpty(0).Max();
                        var newTop = Math.Min(part.Top, memberTop - depth);
                        top = newTop - part.Bottom >= 0.002 ? newTop : null;
                    }
                    if (runTop is not null && (top is null || Math.Abs(top.Value - runTop.Value) > 1e-9))
                    {
                        var rectU0 = uList[i];
                        var rectU1 = uList[i + 1];
                        var rectV1 = vList[j];
                        yield return part with
                        {
                            Footprint = [At(origin, u, v, rectU0, runStart), At(origin, u, v, rectU1, runStart), At(origin, u, v, rectU1, rectV1), At(origin, u, v, rectU0, rectV1)],
                            Top = runTop.Value
                        };
                        runTop = null;
                    }
                    if (top is not null && runTop is null) { runTop = top; runStart = vList[j]; }
                }
            }
        }
    }
}

public sealed partial class DocumentSession
{
    private StructuralElement RequireMember(Guid id) =>
        Document.StructuralElements.FirstOrDefault(item => item.Id == id && item.HasSolid) ?? throw new InvalidDataException("The member does not exist.");

    private void SetCuts(Guid id, List<MemberCut> cuts, string transaction)
    {
        if (cuts.Count > MemberCuts.MaximumCuts) throw new InvalidDataException("A member cannot have so many cuts.");
        Commit(transaction, document =>
        {
            var index = document.StructuralElements.FindIndex(item => item.Id == id);
            document.StructuralElements[index] = document.StructuralElements[index] with { Cuts = cuts.Count == 0 ? null : cuts };
        });
    }

    /// <summary>Adds a cut to a member, as one undoable step.</summary>
    public void AddMemberCut(Guid memberId, MemberCut cut)
    {
        var member = RequireMember(memberId);
        if (cut is null || !cut.IsValid) throw new InvalidDataException("The cut is invalid.");
        if (member.Kind is StructuralElementKind.Planting or StructuralElementKind.Stair or StructuralElementKind.Railing or StructuralElementKind.Ramp ||
            MepElements.IsRound(member.Kind)) throw new InvalidDataException("This kind of element cannot be cut.");
        SetCuts(memberId, [.. member.Cuts ?? [], cut], "Transaction.AddMemberCut");
    }

    public void ClearMemberCuts(Guid memberId)
    {
        var member = RequireMember(memberId);
        if (member.Cuts is not { Count: > 0 }) return;
        SetCuts(memberId, [], "Transaction.ClearMemberCuts");
    }

    /// <summary>
    /// Notches the end of a member: the top <paramref name="depth"/> over the last <paramref name="length"/>, across the whole width or only
    /// the middle <paramref name="widthFraction"/> of it, as a beam is coped to fit against another.
    /// </summary>
    public void AddCope(Guid memberId, bool atStart, double length, double depth, double widthFraction = 1)
    {
        var member = RequireMember(memberId);
        if (member.Kind is StructuralElementKind.Plate) throw new InvalidDataException("A plate has corner and contour cuts, not copes.");
        if (!double.IsFinite(length) || length < 0.005 || length > member.Length / 2 || !double.IsFinite(depth) || depth <= 0 || depth > member.Height ||
            !double.IsFinite(widthFraction) || widthFraction <= 0 || widthFraction > 1) throw new InvalidDataException("The cope is invalid.");
        var half = member.Width * widthFraction / 2;
        AddMemberCut(memberId, atStart ? new MemberCut(0, length, -half, half, depth) : new MemberCut(member.Length - length, member.Length, -half, half, depth));
    }

    /// <summary>Clips a corner off a plate (0 least X and Y, 1 greatest X, 2 greatest of both, 3 greatest Y), <paramref name="a"/> along X by <paramref name="b"/> along Y.</summary>
    public void AddCornerCut(Guid plateId, int corner, double a, double b)
    {
        var plate = RequireMember(plateId);
        if (plate.Kind != StructuralElementKind.Plate) throw new InvalidDataException("Only plates have corner cuts.");
        var width = plate.Footprint.Max(p => p.X) - plate.Footprint.Min(p => p.X);
        var depth = plate.Footprint.Max(p => p.Y) - plate.Footprint.Min(p => p.Y);
        if (corner is < 0 or > 3 || !double.IsFinite(a + b) || a < 0.005 || b < 0.005 || a >= width || b >= depth) throw new InvalidDataException("The corner cut is invalid.");
        var cut = corner switch
        {
            0 => new MemberCut(0, a, 0, b, plate.Height),
            1 => new MemberCut(width - a, width, 0, b, plate.Height),
            2 => new MemberCut(width - a, width, depth - b, depth, plate.Height),
            _ => new MemberCut(0, a, depth - b, depth, plate.Height)
        };
        AddMemberCut(plateId, cut);
    }

    /// <summary>Cuts a rectangular hole all the way through a member, <paramref name="length"/> along U by <paramref name="width"/> along V from (u, v).</summary>
    public void AddContourCut(Guid memberId, double u, double v, double length, double width)
    {
        var member = RequireMember(memberId);
        AddMemberCut(memberId, new MemberCut(u, u + length, v, v + width, member.Height));
    }

    /// <summary>Cuts a slot across the whole width of a member, <paramref name="length"/> long from <paramref name="u"/>, which splits it in two.</summary>
    public void AddCutThrough(Guid memberId, double u, double length)
    {
        var member = RequireMember(memberId);
        AddMemberCut(memberId, new MemberCut(u, u + length, -1000, 1000, member.Height));
    }

    /// <summary>Cuts a member where another one passes through it: the plan of the cutter, from the top of the member down to the cutter's underside.</summary>
    public void CutMemberBy(Guid hostId, Guid cutterId)
    {
        var host = RequireMember(hostId);
        var cutter = RequireMember(cutterId);
        if (hostId == cutterId || host.LevelId != cutter.LevelId) throw new InvalidDataException("Two different members of one level are needed.");
        var (origin, u, v) = MemberCuts.Frame(host);
        var points = ElementGeometry.Parts(cutter).SelectMany(part => part.Footprint).ToArray();
        if (points.Length == 0) throw new InvalidDataException("The cutter has no solid.");
        double Along(Point2 p) => (p.X - origin.X) * u.X + (p.Y - origin.Y) * u.Y;
        double Across(Point2 p) => (p.X - origin.X) * v.X + (p.Y - origin.Y) * v.Y;
        double u0 = points.Min(Along), u1 = points.Max(Along), v0 = points.Min(Across), v1 = points.Max(Across);
        var hostTop = host.Offset + host.Height;
        var cutterBottom = cutter.Offset;
        var cutterTop = cutter.Offset + cutter.Height;
        if (cutterBottom >= hostTop || cutterTop <= host.Offset) throw new InvalidDataException("The cutter does not reach the member.");
        var depth = cutterTop >= hostTop ? Math.Min(host.Height, hostTop - cutterBottom) : host.Height;
        u0 = Math.Max(u0, -1000);
        AddMemberCut(hostId, new MemberCut(u0, Math.Max(u1, u0 + 0.001), v0, Math.Max(v1, v0 + 0.001), depth));
    }

    /// <summary>Shortens a member by moving one of its ends toward the other.</summary>
    public void ShortenMember(Guid memberId, bool atStart, double distance)
    {
        var member = RequireMember(memberId);
        if (UsesLine(member) is false) throw new InvalidDataException("Only members drawn between two points can be shortened.");
        if (!double.IsFinite(distance) || distance <= 0 || distance >= member.Length - 0.05) throw new InvalidDataException("The distance is invalid.");
        var dx = (member.End.X - member.Start.X) / member.Length;
        var dy = (member.End.Y - member.Start.Y) / member.Length;
        Commit("Transaction.ShortenMember", document =>
        {
            var index = document.StructuralElements.FindIndex(item => item.Id == memberId);
            document.StructuralElements[index] = atStart
                ? member with { Start = new Point2(member.Start.X + dx * distance, member.Start.Y + dy * distance) }
                : member with { End = new Point2(member.End.X - dx * distance, member.End.Y - dy * distance) };
        });
    }

    private static bool UsesLine(StructuralElement member) =>
        member.Length >= 0.05 && member.Kind is not (StructuralElementKind.Plate or StructuralElementKind.Mass or StructuralElementKind.Opening or StructuralElementKind.Shaft);

    /// <summary>
    /// Joins two members that meet at an angle: the end of each nearest to where their axes cross is moved to that point, so
    /// they meet axis to axis. The crossing must lie near both members.
    /// </summary>
    public void MiterMembers(Guid firstId, Guid secondId)
    {
        var a = RequireMember(firstId);
        var b = RequireMember(secondId);
        if (firstId == secondId || a.LevelId != b.LevelId || !UsesLine(a) || !UsesLine(b)) throw new InvalidDataException("Two different members of one level are needed.");
        var d1x = a.End.X - a.Start.X;
        var d1y = a.End.Y - a.Start.Y;
        var d2x = b.End.X - b.Start.X;
        var d2y = b.End.Y - b.Start.Y;
        var cross = d1x * d2y - d1y * d2x;
        if (Math.Abs(cross) < 1e-9 * a.Length * b.Length) throw new InvalidDataException("The members are parallel, so they never meet.");
        var t = ((b.Start.X - a.Start.X) * d2y - (b.Start.Y - a.Start.Y) * d2x) / cross;
        var corner = new Point2(a.Start.X + d1x * t, a.Start.Y + d1y * t);
        var reach = Math.Max(2.0, 0.5 * Math.Max(a.Length, b.Length));
        bool NearStart(StructuralElement member) => member.Start.DistanceTo(corner) <= member.End.DistanceTo(corner);
        double Gap(StructuralElement member) => Math.Min(member.Start.DistanceTo(corner), member.End.DistanceTo(corner));
        if (Gap(a) > reach || Gap(b) > reach) throw new InvalidDataException("The members do not meet near their ends.");
        StructuralElement Moved(StructuralElement member) => NearStart(member) ? member with { Start = corner } : member with { End = corner };
        var newA = Moved(a);
        var newB = Moved(b);
        if (newA.Length < 0.05 || newB.Length < 0.05) throw new InvalidDataException("The members would be left too short.");
        Commit("Transaction.MiterMembers", document =>
        {
            document.StructuralElements[document.StructuralElements.FindIndex(item => item.Id == firstId)] = newA;
            document.StructuralElements[document.StructuralElements.FindIndex(item => item.Id == secondId)] = newB;
        });
    }

    /// <summary>Places a pattern of bolts, grouped so they move together: rows by columns, each <paramref name="pitch"/> apart, centred on a point.</summary>
    public IReadOnlyList<Guid> AddBoltPattern(Guid levelId, Point2 centre, int rows, int columns, double pitch, double diameter)
    {
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        if (rows < 1 || columns < 1 || rows * columns > 100 || !double.IsFinite(pitch) || pitch < 0.02 || pitch > 2 || !double.IsFinite(diameter) || diameter < 0.006 || diameter > 0.1)
            throw new InvalidDataException("The bolt pattern is invalid.");
        var group = Guid.NewGuid();
        var ids = new List<Guid>();
        Commit("Transaction.AddBoltPattern", document =>
        {
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                {
                    var at = new Point2(centre.X + (column - (columns - 1) / 2.0) * pitch, centre.Y + (row - (rows - 1) / 2.0) * pitch);
                    var id = Guid.NewGuid();
                    ids.Add(id);
                    document.StructuralElements.Add(new StructuralElement(id, levelId, StructuralElementKind.Bolt, "Perno", at, at, diameter, diameter * 4, 1.0)
                    { Style = ElementStyle.Round, GroupId = rows * columns > 1 ? group : null });
                }
        });
        return ids;
    }
}
