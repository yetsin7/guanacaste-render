namespace Guanacaste3D.Core;

/// <summary>What a design study looks for in the shapes it tries.</summary>
public enum StudyGoal
{
    /// <summary>The least wall for the floor area: a square scores best.</summary>
    Compactness,
    /// <summary>The least facade for the volume enclosed, so height counts too.</summary>
    FacadeToVolume,
    /// <summary>A floor area as close as possible to the one wanted.</summary>
    TargetArea
}

/// <summary>
/// A study of a mass: it tries <see cref="Count"/> rectangles whose width, depth and height fall in the ranges, chosen from a seed so the
/// same study always tries the same shapes, and ranks them by the goal. Shapes whose floor area falls outside the allowed range rank last.
/// </summary>
public sealed record DesignStudy(Guid Id, string Name, Guid MassId, double WidthMin, double WidthMax, double DepthMin, double DepthMax,
    double HeightMin, double HeightMax, StudyGoal Goal, double TargetArea, double AreaMin, double AreaMax, int Count, int Seed)
{
    public bool IsValid =>
        Id != Guid.Empty && MassId != Guid.Empty && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 120 && Enum.IsDefined(Goal) &&
        double.IsFinite(WidthMin + WidthMax + DepthMin + DepthMax + HeightMin + HeightMax + TargetArea + AreaMin + AreaMax) &&
        WidthMin >= 0.5 && WidthMax >= WidthMin && WidthMax <= 1000 && DepthMin >= 0.5 && DepthMax >= DepthMin && DepthMax <= 1000 &&
        HeightMin >= 0.2 && HeightMax >= HeightMin && HeightMax <= 500 && TargetArea >= 0 && AreaMin >= 0 && AreaMax >= 0 &&
        (AreaMax == 0 || AreaMax >= AreaMin) && Count is >= 1 and <= DesignStudies.MaximumCount && (Goal != StudyGoal.TargetArea || TargetArea >= 1);
}

/// <summary>One shape a study tried, ranked from 1 (the best) with the measures it was judged by; the lower the cost, the better.</summary>
public sealed record StudyOutcome(int Rank, int Index, double Width, double Depth, double Height, double Area, double Volume, double FacadeArea,
    double Compactness, double Cost, bool Feasible);

public static class DesignStudies
{
    public const int MaximumCount = 2000;
    public const int MaximumStudies = 100;
    private const double Snap = 0.05;

    // A small generator whose results never change from one run or machine to another, unlike a library one.
    private static double Unit(int seed, int index, int stream)
    {
        var x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)index * 0xBF58476D1CE4E5B9UL + (ulong)stream * 0x94D049BB133111EBUL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (x >> 11) / (double)(1UL << 53);
    }

    private static double Pick(double low, double high, double unit) =>
        Math.Clamp(Math.Round((low + (high - low) * unit) / Snap) * Snap, low, high);

    /// <summary>Tries the shapes and returns them ranked: those inside the allowed area first, then the lowest cost, then the order tried.</summary>
    public static IReadOnlyList<StudyOutcome> Evaluate(DesignStudy study)
    {
        ArgumentNullException.ThrowIfNull(study);
        if (!study.IsValid) throw new InvalidDataException("The study is invalid.");
        var tried = new List<StudyOutcome>();
        for (var index = 0; index < study.Count; index++)
        {
            var width = Pick(study.WidthMin, study.WidthMax, Unit(study.Seed, index, 1));
            var depth = Pick(study.DepthMin, study.DepthMax, Unit(study.Seed, index, 2));
            var height = Pick(study.HeightMin, study.HeightMax, Unit(study.Seed, index, 3));
            var area = width * depth;
            var perimeter = 2 * (width + depth);
            var facade = perimeter * height;
            var volume = area * height;
            var compactness = 4 * Math.Sqrt(area) / perimeter;
            var cost = study.Goal switch
            {
                StudyGoal.Compactness => 1 - compactness,
                StudyGoal.FacadeToVolume => facade / volume,
                _ => Math.Abs(area - study.TargetArea) / study.TargetArea
            };
            var feasible = area >= study.AreaMin - 1e-9 && (study.AreaMax == 0 || area <= study.AreaMax + 1e-9);
            tried.Add(new StudyOutcome(0, index, width, depth, height, area, volume, facade, compactness, cost, feasible));
        }
        return tried.OrderByDescending(item => item.Feasible).ThenBy(item => Math.Round(item.Cost, 9)).ThenBy(item => item.Index)
            .Select((item, position) => item with { Rank = position + 1 }).ToList();
    }
}

public sealed partial class DocumentSession
{
    public Guid AddStudy(string name, Guid massId, double widthMin, double widthMax, double depthMin, double depthMax, double heightMin, double heightMax,
        StudyGoal goal, double targetArea, double areaMin, double areaMax, int count, int seed)
    {
        if (!Document.StructuralElements.Any(item => item.Id == massId && item.Kind == StructuralElementKind.Mass)) throw new InvalidDataException("The mass does not exist.");
        var study = new DesignStudy(Guid.NewGuid(), name?.Trim() ?? string.Empty, massId, widthMin, widthMax, depthMin, depthMax, heightMin, heightMax, goal, targetArea, areaMin, areaMax, count, seed);
        if (!study.IsValid) throw new InvalidDataException("The study is invalid.");
        if (Document.Studies.Count >= DesignStudies.MaximumStudies) throw new InvalidDataException("The project has too many studies.");
        if (Document.Studies.Any(item => item.Name.Equals(study.Name, StringComparison.CurrentCultureIgnoreCase))) throw new InvalidDataException("A study with that name already exists.");
        Commit("Transaction.AddStudy", document => document.Studies.Add(study));
        return study.Id;
    }

    public void RemoveStudy(Guid id)
    {
        if (Document.Studies.All(item => item.Id != id)) throw new InvalidDataException("The study does not exist.");
        Commit("Transaction.RemoveStudy", document => document.Studies.RemoveAll(item => item.Id == id));
    }

    private (DesignStudy Study, StructuralElement Mass) RequireStudy(Guid id)
    {
        var study = Document.Studies.FirstOrDefault(item => item.Id == id) ?? throw new InvalidDataException("The study does not exist.");
        var mass = Document.StructuralElements.FirstOrDefault(item => item.Id == study.MassId && item.Kind == StructuralElementKind.Mass) ?? throw new InvalidDataException("The mass does not exist.");
        return (study, mass);
    }

    /// <summary>Gives the study's mass the shape of one outcome, keeping its centre, as one undoable step.</summary>
    public void ApplyStudyOutcome(Guid studyId, int rank)
    {
        var (study, mass) = RequireStudy(studyId);
        var outcome = DesignStudies.Evaluate(study).FirstOrDefault(item => item.Rank == rank) ?? throw new InvalidDataException("The outcome does not exist.");
        var footprint = mass.Footprint;
        var centreX = (footprint.Max(p => p.X) + footprint.Min(p => p.X)) / 2;
        var centreY = (footprint.Max(p => p.Y) + footprint.Min(p => p.Y)) / 2;
        Commit("Transaction.ApplyStudyOutcome", document =>
        {
            var index = document.StructuralElements.FindIndex(item => item.Id == mass.Id);
            document.StructuralElements[index] = document.StructuralElements[index] with
            {
                Start = new Point2(centreX - outcome.Width / 2, centreY - outcome.Depth / 2),
                End = new Point2(centreX + outcome.Width / 2, centreY + outcome.Depth / 2),
                Height = outcome.Height
            };
        });
    }

    /// <summary>
    /// Puts the best outcomes next to the original in a design option set, so each can be looked at by itself and compared: the
    /// original is the primary option and every outcome is another option with a mass of its own. Returns how many were made.
    /// </summary>
    public int AddStudyOutcomesAsOptions(Guid studyId, int top)
    {
        var (study, mass) = RequireStudy(studyId);
        if (top is < 1 or > 12) throw new InvalidDataException("Choose between 1 and 12 outcomes.");
        var chosen = DesignStudies.Evaluate(study).Where(item => item.Feasible).Take(top).ToList();
        if (chosen.Count == 0) throw new InvalidDataException("No outcome fits the allowed area.");
        var footprint = mass.Footprint;
        var centreX = (footprint.Max(p => p.X) + footprint.Min(p => p.X)) / 2;
        var centreY = (footprint.Max(p => p.Y) + footprint.Min(p => p.Y)) / 2;
        using var group = BeginGroup("Transaction.AddStudyOutcomesAsOptions");
        var set = AddOptionSet(UniqueSetName($"Estudio {study.Name}"), "Original");
        AssignToOption([mass.Id], Document.OptionSets.Single(item => item.Id == set).PrimaryOptionId);
        foreach (var outcome in chosen)
        {
            var label = $"#{outcome.Rank} {outcome.Width:0.##}×{outcome.Depth:0.##}×{outcome.Height:0.##}";
            var option = AddDesignOption(set, label);
            var id = AddStructuralElement(mass.LevelId, StructuralElementKind.Mass, $"{mass.Name} {label}",
                new Point2(centreX - outcome.Width / 2, centreY - outcome.Depth / 2), new Point2(centreX + outcome.Width / 2, centreY + outcome.Depth / 2));
            UpdateStructuralElement(id, $"{mass.Name} {label}", mass.Width, outcome.Height, mass.Offset, ElementStyle.Default);
            AssignToOption([id], option);
        }
        group.Assimilate();
        return chosen.Count;
    }
}
