using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Routes the Modify ribbon's wall editing commands. Commands that need only the selected wall ask for numbers;
    /// the others arm a wall pick and finish on the clicks.
    /// </summary>
    private bool TryExecuteWallEditCommand(string commandId)
    {
        switch (commandId)
        {
            case "Offset": OffsetSelectedWall(); return true;
            case "Array": ArraySelectedWall(); return true;
            case "Pin": TogglePinSelectedWall(); return true;
            case "MirrorPickAxis": BeginMirrorPick(); return true;
            case "Split": BeginSplitPick(); return true;
            case "TrimExtend": BeginTrimPick(); return true;
            case "Align": BeginAlignPick(); return true;
            case "MatchProperties": BeginMatchPick(); return true;
            case "FlipWall": FlipSelectedWall(); return true;
            case "AttachBase": AttachSelectedWallBase(); return true;
            case "AttachTop":
            case "AttachTopBase": AttachSelectedWallTop(); return true;
            case "DetachTopBase":
            case "DetachAll": DetachSelectedWallTop(); return true;
            default: return false;
        }
    }

    /// <summary>Runs an edit, reporting a refused one in the status line instead of changing anything.</summary>
    private bool RunWallEdit(Action edit, string statusKey)
    {
        try
        {
            edit();
            StatusText.Text = GetText(statusKey);
            RefreshAll();
            return true;
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
            return false;
        }
    }

    /// <summary>Where the pointer is in the model, for commands that need the clicked point as well as the wall.</summary>
    private Point2 PointerModelPoint() => CanvasToWorld(Mouse.GetPosition(PlanCanvas));

    /// <summary>Reads a number the way it was typed, with either a decimal point or a decimal comma.</summary>
    private static bool TryReadNumber(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value);

    private void OffsetSelectedWall()
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        var text = TextPrompt.Ask(this, GetText("Prompt.Offset.Title"), GetText("Prompt.Offset.Label"),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"), "1");
        if (text is null) return;
        if (!TryReadNumber(text, out var distance)) { StatusText.Text = GetText("Status.InvalidNumber"); return; }
        RunWallEdit(() => _selectedWallId = _session.OffsetWalls([id], distance, copy: true)[0], "Status.WallOffset");
    }

    private void ArraySelectedWall()
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        var text = TextPrompt.Ask(this, GetText("Prompt.Array.Title"), GetText("Prompt.Array.Label"),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"), "3; 0; 3");
        if (text is null) return;
        var parts = text.Split([';', ' '], StringSplitOptions.RemoveEmptyEntries);
        // "r; 6; 60": a radial array of 6 members, 60 degrees apart, about the start of the selected wall.
        if (parts.Length == 3 && parts[0].Equals("r", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadNumber(parts[1], out var members) || !TryReadNumber(parts[2], out var step) || members != Math.Floor(members))
            {
                StatusText.Text = GetText("Status.InvalidNumber");
                return;
            }
            var center = _session.Document.Walls.Single(wall => wall.Id == id).Start;
            RunWallEdit(() => _session.ArrayWallsRadial([id], (int)members, center, step), "Status.WallArrayed");
            return;
        }
        if (parts.Length != 3 || !TryReadNumber(parts[0], out var count) || !TryReadNumber(parts[1], out var deltaX) ||
            !TryReadNumber(parts[2], out var deltaY) || count != Math.Floor(count))
        {
            StatusText.Text = GetText("Status.InvalidNumber");
            return;
        }
        RunWallEdit(() => _session.ArrayWallsLinear([id], (int)count, deltaX, deltaY), "Status.WallArrayed");
    }

    /// <summary>Ties the selected wall's top to the underside of the slab or roof above it.</summary>
    private void AttachSelectedWallTop()
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        RunWallEdit(() => _session.AttachWallTopToSlabAbove(id), "Status.WallTopAttached");
    }

    /// <summary>Takes the base of the selected wall to the top of the slab under it.</summary>
    private void AttachSelectedWallBase()
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        RunWallEdit(() => _session.AttachWallBaseToSlab(id), "Status.WallBaseAttached");
    }

    private void FlipSelectedWall()
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        RunWallEdit(() => _session.FlipWalls([id]), "Status.SelectedWallFlipped");
    }

    /// <summary>Releases the top constraint of the selected wall; it keeps its current height.</summary>
    private void DetachSelectedWallTop()
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        RunWallEdit(() =>
        {
            var mark = _session.MarkHistory();
            _session.SetWallTopConstraint(id, null);
            if (_session.Document.Walls.FirstOrDefault(wall => wall.Id == id) is { BaseOffset: not 0 }) _session.SetWallBaseOffset(id, 0);
            _session.CollapseHistorySince(mark, "Transaction.DetachWall");
        }, "Status.WallTopDetached");
    }

    private void TogglePinSelectedWall()
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        var pinned = !_session.Document.Walls.Single(wall => wall.Id == id).Pinned;
        RunWallEdit(() => _session.SetWallsPinned([id], pinned), pinned ? "Status.WallPinned" : "Status.WallUnpinned");
    }

    private void BeginMirrorPick()
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        BeginWallPick(axis =>
        {
            var line = _session.Document.Walls.Single(wall => wall.Id == axis);
            if (axis == id) { StatusText.Text = GetText("Status.PickOtherWall"); return false; }
            _pickWallAction = null;
            return RunWallEdit(() => _selectedWallId = _session.MirrorWalls([id], line.Start, line.End)[0], "Status.WallMirrored");
        }, "Status.PickMirrorAxis");
    }

    private void BeginSplitPick() =>
        BeginWallPick(wall =>
        {
            _pickWallAction = null;
            return RunWallEdit(() => _session.SplitWall(wall, PointerModelPoint()), "Status.WallSplit");
        }, "Status.PickSplitWall");

    private void BeginTrimPick()
    {
        Guid? first = null;
        var firstPick = default(Point2);
        BeginWallPick(wall =>
        {
            if (first is null)
            {
                first = wall;
                firstPick = PointerModelPoint();
                StatusText.Text = GetText("Status.PickTrimSecond");
                return false;
            }
            var secondPick = PointerModelPoint();
            _pickWallAction = null;
            return RunWallEdit(() => _session.TrimToCorner(first.Value, firstPick, wall, secondPick), "Status.WallsTrimmed");
        }, "Status.PickTrimFirst");
    }

    private void BeginAlignPick()
    {
        (Guid Wall, int Side)? reference = null;
        BeginWallPick(wall =>
        {
            var side = FaceNearPointer(wall);
            if (reference is null)
            {
                reference = (wall, side);
                StatusText.Text = GetText("Status.PickAlignMoving");
                return false;
            }
            _pickWallAction = null;
            return RunWallEdit(() => _session.AlignWall(wall, side, reference.Value.Wall, reference.Value.Side), "Status.WallAligned");
        }, "Status.PickAlignReference");
    }

    /// <summary>The face of a wall nearest to the pointer: -1 right, 1 left, or 0 for the centerline when the click is near it.</summary>
    private int FaceNearPointer(Guid wallId)
    {
        var wall = _session.Document.Walls.Single(candidate => candidate.Id == wallId);
        var pointer = PointerModelPoint();
        var (normalX, normalY) = (-(wall.End.Y - wall.Start.Y) / wall.Length, (wall.End.X - wall.Start.X) / wall.Length);
        var across = (pointer.X - wall.Start.X) * normalX + (pointer.Y - wall.Start.Y) * normalY;
        return Math.Abs(across) < wall.Thickness / 8 ? 0 : Math.Sign(across);
    }

    private void BeginMatchPick()
    {
        Guid? source = null;
        BeginWallPick(wall =>
        {
            if (source is null)
            {
                source = wall;
                StatusText.Text = GetText("Status.PickMatchTarget");
                return false;
            }
            return RunWallEdit(() => _session.MatchWallProperties(source.Value, [wall]), "Status.WallMatched");
        }, "Status.PickMatchSource");
    }
}
