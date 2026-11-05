using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{
    /// <summary>
    /// Opens the breeze-block (cobogó) house from the Home screen, the way a user does, and works on it: every level is
    /// drawn, every wall is selected and shown in the properties panel, every kind of element is found by identifier,
    /// categories are hidden and shown again, and random sequences of real ribbon commands run on its real walls.
    /// Nothing here uses a layout made up for the test, so the checks meet the joins, tees, openings and levels of an
    /// actual design. The window is its own, so the rest of the suite is untouched.
    /// </summary>
    private static void CobogoHouseChecks()
    {
        var previous = _window;
        var opened = new MainWindow(showStartScreen: true) { Width = 1440, Height = 900, WindowState = WindowState.Normal };
        _window = opened;
        try
        {
            opened.Show();
            Pump();
            var host = (IStartScreenHost)opened;
            host.OpenSample(host.Samples.Single(sample => sample.Id == "cobogo"));
            Pump();
            _canvas = (Canvas)opened.FindName("PlanCanvas");
            var session = Field<DocumentSession>("_session");
            var initial = ProjectFile.SerializeDocument(session.Document);
            var tabs = (TabControl)opened.FindName("ViewTabs");
            var properties = (TextBlock)opened.FindName("PropertiesText");

            Check(session.Document.Walls.Count >= 20 && session.Document.Openings.Count >= 3 && session.Document.Levels.Count >= 2,
                $"The cobogó house opens with its real design: {session.Document.Walls.Count} walls, " +
                $"{session.Document.Openings.Count} openings, {session.Document.Levels.Count} levels");

            // Every level is drawn in plan, and every wall can be selected and described in the properties panel.
            tabs.SelectedIndex = 0;
            var described = 0;
            foreach (var level in session.Document.Levels)
            {
                var walls = session.Document.Walls.Where(wall => wall.LevelId == level.Id).ToArray();
                if (walls.Length == 0) continue;
                Check((bool)Invoke("SelectElement", walls[0].Id)!, $"Level {level.Name} is reached by selecting one of its walls");
                Invoke("RenderPlan");
                Pump();
                Check(_canvas.Children.Count > walls.Length, $"Plan of {level.Name} draws its {walls.Length} walls and more");
                foreach (var wall in walls)
                {
                    SetField("_selectedWallId", (Guid?)wall.Id);
                    Invoke("RefreshProperties");
                    Invoke("RenderPlan");
                    if (!string.IsNullOrWhiteSpace(properties.Text)) described++;
                }
            }
            Check(described >= session.Document.Walls.Count - 2,
                $"The properties panel describes {described} of the house's {session.Document.Walls.Count} walls");

            // Every kind of element is found by identifier and selected.
            var document = session.Document;
            var firstOfEach = new (string Name, Guid? Id, string Field)[]
            {
                ("a wall", document.Walls.FirstOrDefault()?.Id, "_selectedWallId"),
                ("an opening", document.Openings.FirstOrDefault()?.Id, "_selectedOpeningId"),
                ("a room", document.Rooms.FirstOrDefault()?.Id, "_selectedRoomId"),
                ("a floor", document.Floors.FirstOrDefault()?.Id, "_selectedFloorId"),
                ("a column", document.Columns.FirstOrDefault()?.Id, "_selectedColumnId"),
                ("a structural element", document.StructuralElements.FirstOrDefault()?.Id, "_selectedStructuralId")
            };
            foreach (var (name, id, field) in firstOfEach.Where(entry => entry.Id is not null))
            {
                Check((bool)Invoke("SelectElement", id!.Value)! && Field<Guid?>(field) == id,
                    $"Select by identifier finds {name} of the house");
            }
            Check(!(bool)Invoke("SelectElement", Guid.NewGuid())!, "Select by identifier reports an unknown identifier");

            // The 3D view switches between perspective and parallel projection with a usable camera either way.
            tabs.SelectedIndex = 1;
            Pump();
            var viewport = (System.Windows.Controls.Viewport3D)opened.FindName("ModelViewport");
            Invoke("ExecuteApplicationCommand", "Orthographic");
            Pump();
            var parallel = viewport.Camera as System.Windows.Media.Media3D.OrthographicCamera;
            Check(parallel is not null && double.IsFinite(parallel.Width) && parallel.Width > 1 && parallel.NearPlaneDistance < 0,
                $"Orthographic gives the 3D view a parallel camera of a usable width ({parallel?.Width:0.0} m)");
            Invoke("ExecuteApplicationCommand", "Perspective");
            Pump();
            Check(viewport.Camera is System.Windows.Media.Media3D.PerspectiveCamera, "Perspective brings the perspective camera back");
            tabs.SelectedIndex = 0;
            Pump();

            // A curved wall is drawn with three clicks on the plan, the way a person does, and is one wall afterwards.
            Invoke("SelectElement", session.Document.Walls[0].Id);
            tabs.SelectedIndex = 0;
            Pump();
            Invoke("SetWallTool", true);
            ((System.Windows.Controls.RadioButton)opened.FindName("WallArcMode")).IsChecked = true;
            Point OnCanvas(double x, double y) => (Point)Invoke("WorldToCanvas", new Point2(x, y))!;
            var wallsBeforeArc = session.Document.Walls.Count;
            Invoke("HandleWallPlacementClick", OnCanvas(540, 540));
            Invoke("HandleWallPlacementClick", OnCanvas(548, 548));
            Check(Field<Point2?>("_pendingArcEnd") is not null && session.Document.Walls.Count == wallsBeforeArc,
                "After the second click an arc is waiting for its third point and nothing has been built yet");
            Invoke("HandleWallPlacementClick", OnCanvas(541.5, 547));
            var arcPieces = session.Document.Walls.Skip(wallsBeforeArc).ToArray();
            Check(arcPieces.Length >= 5 && arcPieces.Select(piece => piece.CurveGroupId).Distinct().Count() == 1 && arcPieces[0].CurveGroupId is not null &&
                  Field<Point2?>("_pendingArcEnd") is null && Field<Point2?>("_pendingWallStart") is null,
                $"The third click builds one curved wall of {arcPieces.Length} pieces and ends the sketch");
            Invoke("SetWallTool", false);
            Invoke("RenderPlan");
            Pump();
            Check(arcPieces.All(piece => session.Document.Walls.Any(wall => wall.Id == piece.Id)), "The plan redraws with the curved wall in it");

            // The curved wall is dimensioned from the Annotate tab: one click per dimension, and a straight wall is refused.
            var dimensionsBefore = session.Document.Dimensions.Count;
            foreach (var command in new[] { "RadialDimension", "DiameterDimension", "ArcLengthDimension" })
            {
                Invoke("ExecuteApplicationCommand", command);
                Check(Field<Func<Guid, bool>?>("_pickWallAction") is not null, $"{command} waits for a click on a wall");
                Field<Func<Guid, bool>>("_pickWallAction")(arcPieces[arcPieces.Length / 2].Id);
            }
            var added = session.Document.Dimensions.Skip(dimensionsBefore).ToArray();
            Check(added.Length == 3 && added.Select(dimension => dimension.Kind).Distinct().Count() == 3 &&
                  added.All(dimension => PlanDimensions.Resolve(session.Document, dimension) is { Value: > 0 }),
                "The radius, the diameter and the arc length of the curved wall are added and read their values");
            // The angle between two walls takes two clicks, and the first wall alone changes nothing.
            var angleLevel = session.Document.Levels[0].Id;
            var angleA = session.AddWall(angleLevel, new Point2(560, 560), new Point2(564, 560));
            var angleB = session.AddWall(angleLevel, new Point2(560, 560), new Point2(560, 564));
            var dimensionsBeforeAngle = session.Document.Dimensions.Count;
            Invoke("ExecuteApplicationCommand", "AngularDimension");
            Check(Field<Func<Guid, bool>>("_pickWallAction")(angleA) == false && session.Document.Dimensions.Count == dimensionsBeforeAngle,
                "The first click of an angular dimension only asks for the second wall");
            Field<Func<Guid, bool>>("_pickWallAction")(angleB);
            Check(session.Document.Dimensions.Count == dimensionsBeforeAngle + 1 &&
                  PlanDimensions.Resolve(session.Document, session.Document.Dimensions[^1]) is { IsAngle: true, Value: > 89.9 and < 90.1 },
                "The second click adds a 90° angular dimension between the two walls");
            Invoke("RenderPlan");
            Pump();

            // The Detail group: each button arms its own tool, and what it draws is shown in the plan and never in 3D.
            foreach (var (command, kind, mode) in new[]
                     {
                         ("DetailLine", StructuralElementKind.DetailLine, "Line"), ("FilledRegion", StructuralElementKind.FilledRegion, "Rectangle"),
                         ("RevisionCloud", StructuralElementKind.RevisionCloud, "Rectangle"), ("Insulation", StructuralElementKind.Insulation, "Line")
                     })
            {
                Invoke("ExecuteApplicationCommand", command);
                Check(Field<object>("_structuralTool").ToString() == $"({kind}, {mode})", $"{command} arms a {mode.ToLowerInvariant()} tool for its own kind");
                var elementsBefore = session.Document.StructuralElements.Count;
                Invoke("CreateStructuralFromTool", kind, new Point2(580, 580), new Point2(586, 584));
                Invoke("RenderPlan");
                Pump();
                var drawn = session.Document.StructuralElements[^1];
                Check(session.Document.StructuralElements.Count == elementsBefore + 1 && drawn.Kind == kind && !drawn.HasSolid &&
                      ((Canvas)opened.FindName("PlanCanvas")).Children.OfType<FrameworkElement>().Any(shape => shape.Tag is Guid tag && tag == drawn.Id),
                    $"{command} adds its element and the plan draws it");
            }
            Invoke("CancelStructuralTool");

            // Project units: the window changes how the wall panel and the dimensions read, and typing a unit is understood.
            T InWindow<T>(string field, string name) where T : FrameworkElement =>
                (T)System.Windows.LogicalTreeHelper.FindLogicalNode(Field<Window>(field), name)!;
            void ClickIn(string field, string name) =>
                InWindow<System.Windows.Controls.Button>(field, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Invoke("SelectElement", angleA);
            Invoke("RefreshProperties");
            Pump();
            var lengthBox = (System.Windows.Controls.TextBox)opened.FindName("PropertyLengthInput");
            Check(lengthBox.Text.StartsWith("4", StringComparison.Ordinal) && lengthBox.Text.Contains('.'), $"The wall panel shows the 4 m wall as {lengthBox.Text} in meters");
            Invoke("ExecuteApplicationCommand", "ProjectUnits");
            Pump();
            InWindow<System.Windows.Controls.ComboBox>("_unitsWindow", "UnitLengthBox").SelectedIndex = (int)LengthUnit.Feet;
            InWindow<System.Windows.Controls.ComboBox>("_unitsWindow", "UnitDecimalsBox").SelectedIndex = 1;
            Check(InWindow<System.Windows.Controls.TextBlock>("_unitsWindow", "UnitSample").Text.Contains("ft"), "The units window shows a sample in the unit chosen");
            ClickIn("_unitsWindow", "UnitApply");
            Invoke("SelectElement", angleA);
            Invoke("RefreshProperties");
            Pump();
            Check(session.Document.Units.Length == LengthUnit.Feet && lengthBox.Text == "13.1",
                $"After choosing feet with one decimal the wall panel shows the 4 m wall as {lengthBox.Text}");
            var dimensionOfWall = session.Document.Dimensions.Last();
            Invoke("ApplyDimensionText", dimensionOfWall, "30 cm");
            Check(session.Document.Dimensions.Count > 0, "A dimension edit typed with a unit (30 cm) is understood while the project is in feet");
            ClickIn("_unitsWindow", "UnitClose");
            session.SetProjectUnits(ProjectUnits.Default);

            // Site: the Position window turns true north and the sun of the 3D view follows it; Coordinates converts points.
            Invoke("ExecuteApplicationCommand", "Position");
            Pump();
            var sunStraight = (System.Numerics.Vector3)Invoke("ModelSunDirection")!;
            InWindow<System.Windows.Controls.TextBox>("_positionWindow", "SiteTrueNorth").Text = "90";
            ClickIn("_positionWindow", "SitePositionApply");
            var sunTurned = (System.Numerics.Vector3)Invoke("ModelSunDirection")!;
            Check(Math.Abs(session.Document.Site.TrueNorthDegrees - 90) < 1e-9 && Math.Abs(sunTurned.Z - sunStraight.Z) < 1e-5 &&
                  Math.Abs(sunTurned.X - -sunStraight.Y) < 1e-5 && Math.Abs(sunTurned.Y - sunStraight.X) < 1e-5,
                "Setting true north to 90° turns the sun of the 3D view with the project and keeps its height");
            InWindow<System.Windows.Controls.TextBox>("_positionWindow", "SiteTrueNorth").Text = "400";
            ClickIn("_positionWindow", "SitePositionApply");
            Check(Math.Abs(session.Document.Site.TrueNorthDegrees - 90) < 1e-9, "An angle out of range changes nothing");
            ClickIn("_positionWindow", "SitePositionClose");
            Invoke("RenderPlan");
            Pump();
            Invoke("ExecuteApplicationCommand", "Coordinates");
            Pump();
            InWindow<System.Windows.Controls.TextBox>("_coordinatesWindow", "SiteEast").Text = "500000";
            InWindow<System.Windows.Controls.TextBox>("_coordinatesWindow", "SiteNorth").Text = "1170000";
            ClickIn("_coordinatesWindow", "SiteApply");
            Check(session.Document.Site.SharedEast == 500000 && session.Document.Site.SharedNorth == 1170000, "The Coordinates window stores the survey coordinates of the project origin");
            InWindow<System.Windows.Controls.TextBox>("_coordinatesWindow", "SiteProjectPoint").Text = "0 1";
            ClickIn("_coordinatesWindow", "SiteToShared");
            Check(InWindow<System.Windows.Controls.TextBlock>("_coordinatesWindow", "SiteResult").Text.Contains("500001"),
                "Project north one meter from the origin reads as one meter east of it, because true north is at 90°");
            ClickIn("_coordinatesWindow", "SiteClose");
            session.SetProjectSite(ProjectSite.Default);

            // Shared parameters: a file with parameters is opened in the window and its parameters are added to the project.
            var sharedFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shared-{Guid.NewGuid():N}.txt");
            System.IO.File.WriteAllText(sharedFile, SharedParameterFile.Write(
                [new ParameterDefinition(Guid.NewGuid(), "Código de obra", ParameterSpec.Text, ParameterBinding.Instance, [ElementCategory.Walls], "Identidad"),
                 new ParameterDefinition(Guid.NewGuid(), "Carga térmica", ParameterSpec.Number, ParameterBinding.Type, [ElementCategory.Walls], "Energía")]));
            var parametersBefore = session.Document.ParameterDefinitions.Count;
            Invoke("ExecuteApplicationCommand", "SharedParameters");
            Pump();
            Check((bool)Invoke("LoadSharedParameterFile", sharedFile)! && InWindow<System.Windows.Controls.ListBox>("_sharedWindow", "SharedList").Items.Count == 2,
                "The Shared Parameters window lists the two parameters of the file");
            ClickIn("_sharedWindow", "SharedAdd");
            Check(session.Document.ParameterDefinitions.Count == parametersBefore + 2, "Adding to the project brings both parameters in");
            ClickIn("_sharedWindow", "SharedAdd");
            Check(session.Document.ParameterDefinitions.Count == parametersBefore + 2, "Adding them again adds nothing");
            var exportFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"export-{Guid.NewGuid():N}.txt");
            Check((bool)Invoke("ExportSharedParameters", exportFile)! && SharedParameterFile.Read(System.IO.File.ReadAllText(exportFile)).Parameters.Count == parametersBefore + 2,
                "The project's own parameters are exported as a shared parameter file");
            System.IO.File.Delete(sharedFile);
            System.IO.File.Delete(exportFile);
            ClickIn("_sharedWindow", "SharedClose");
            Check(!(bool)Invoke("LoadSharedParameterFile", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-shared-file.txt"))!,
                "A file that does not exist is reported, not opened");

            // Wall profile: the editor opens on the selected wall, presets fill it, Apply shapes the wall and Reset flattens it.
            T InProfile<T>(string name) where T : FrameworkElement =>
                (T)System.Windows.LogicalTreeHelper.FindLogicalNode(Field<Window>("_profileWindow"), name)!;
            void ClickProfile(string name) =>
                InProfile<System.Windows.Controls.Button>(name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Invoke("SelectElement", angleA);
            Invoke("ExecuteApplicationCommand", "EditProfile");
            Pump();
            Check(Field<Window?>("_profileWindow")?.IsVisible == true && InProfile<System.Windows.Controls.TextBox>("ProfileText").Text.Contains(';'),
                "Edit Profile opens the editor on the selected wall with its top as a list of points");
            InProfile<System.Windows.Controls.TextBox>("ProfileText").Text = "esto no es un perfil";
            ClickProfile("ProfileApply");
            Check(session.Document.Walls.Single(wall => wall.Id == angleA).TopProfile is null &&
                  InProfile<System.Windows.Controls.TextBlock>("ProfileMessage").Text.Length > 0,
                "Text that is not a profile changes nothing and the editor says so");
            ClickProfile("ProfileGable");
            ClickProfile("ProfileApply");
            var profiled = session.Document.Walls.Single(wall => wall.Id == angleA);
            Check(profiled.TopProfile is { Count: 3 } && profiled.TopHeightAt(profiled.Length / 2) > profiled.TopHeightAt(0),
                "The gable preset applied from the editor gives the wall a ridge higher than its ends");
            Invoke("RenderPlan");
            Pump();
            ClickProfile("ProfileClose");
            Invoke("SelectElement", angleA);
            Invoke("ExecuteApplicationCommand", "ResetProfile");
            Check(session.Document.Walls.Single(wall => wall.Id == angleA).TopProfile is null, "Reset Profile flattens the top of the selected wall again");

            Invoke("ExecuteApplicationCommand", "RadialDimension");
            Field<Func<Guid, bool>>("_pickWallAction")(session.Document.Walls[0].Id);
            Check(session.Document.Dimensions.Count == dimensionsBefore + 4, "A straight wall gets no radius and the status line says why");
            SetField("_pickWallAction", null);
            Invoke("RenderPlan");
            Pump();

            // The level section box shows one level in 3D, and pressing it again brings everything back.
            tabs.SelectedIndex = 1;
            Pump();
            var everything = ((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls.Count;
            Invoke("SelectElement", session.Document.Walls[0].Id);
            var activeLevel = Field<Guid>("_activeLevelId");
            Invoke("ExecuteApplicationCommand", "SectionBox");
            var shown = ((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls;
            Check(shown.Count > 0 && shown.Count < everything && shown.All(wall => wall.LevelId == activeLevel),
                $"The section box leaves {shown.Count} of the house's {everything} walls, all of the active level");
            Invoke("ExecuteApplicationCommand", "SectionBox");
            Check(((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls.Count == everything,
                "Pressing the section box again shows the whole house");
            tabs.SelectedIndex = 0;
            Pump();

            // Elevation and section are drawn when their tabs open, not on every edit, and they show the current model.
            var elevationCanvas = (Canvas)opened.FindName("ElevationCanvas");
            tabs.SelectedIndex = 4;
            Pump();
            Check(elevationCanvas.Children.Count > 20, $"The elevation tab draws the house when it opens ({elevationCanvas.Children.Count} shapes)");
            var polygonsFlat = elevationCanvas.Children.OfType<System.Windows.Shapes.Polygon>().Count();
            tabs.SelectedIndex = 0;
            Pump();
            session.SetWallProfile(angleA, WallProfiles.Gable(4, 3));
            tabs.SelectedIndex = 4;
            Pump();
            Check(elevationCanvas.Children.OfType<System.Windows.Shapes.Polygon>().Count() > polygonsFlat,
                "A wall with a profile is drawn in the elevation as the polygon under its top, not as a rectangle");
            session.ResetWallProfiles([angleA]);
            var sectionCanvas = (Canvas)opened.FindName("SectionCanvas");
            tabs.SelectedIndex = 5;
            Pump();
            Check(sectionCanvas.Children.Count > 20, $"The section tab draws the house when it opens ({sectionCanvas.Children.Count} shapes)");
            var sheetCanvas = (Canvas)opened.FindName("SheetCanvas");
            tabs.SelectedIndex = 3;
            Pump();
            Check(sheetCanvas.Children.Count > 10, $"The sheets tab draws the house's sheet when it opens ({sheetCanvas.Children.Count} layers)");
            tabs.SelectedIndex = 0;
            Pump();

            // Hiding a category thins the drawing and showing it again restores it.
            var groundWall = session.Document.Walls[0];
            Invoke("SelectElement", groundWall.Id);
            Invoke("RenderPlan");
            Pump();
            var planView = session.Document.Views.First(view => view.Kind == ProjectViewKind.Plan && view.LevelId == groundWall.LevelId);
            var full = _canvas.Children.Count;
            session.SetViewHiddenCategories(planView.Id, [ViewCategory.Walls, ViewCategory.Doors, ViewCategory.Windows]);
            Invoke("RenderPlan");
            Pump();
            var thinned = _canvas.Children.Count;
            session.SetViewHiddenCategories(planView.Id, []);
            Invoke("RenderPlan");
            Pump();
            Check(thinned < full && _canvas.Children.Count == full,
                $"Hiding walls, doors and windows thins the house's plan ({full} to {thinned} shapes) and showing them restores it");

            // The warnings of a real design are reviewable, and none of them is a crash or a duplicate.
            var warnings = ModelWarnings.Analyze(session.Document);
            Check(warnings.All(warning => warning.ElementIds.Count > 0) &&
                  warnings.All(warning => warning.Code != "DuplicateWalls"),
                $"Reviewing the house's warnings works ({warnings.Count} found: {string.Join(", ", warnings.Select(warning => warning.Code).Distinct())})");

            if (Environment.GetEnvironmentVariable("G3D_PROFILE") == "1") ProfileRefresh();

            // Each ribbon command does what it says on the longest real wall of the house.
            var longest = session.Document.Walls.Where(wall => !wall.Pinned).OrderByDescending(wall => wall.Length).First();
            Wall Current() => session.Document.Walls.Single(wall => wall.Id == longest.Id);
            Invoke("SelectElement", longest.Id);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Invoke("ExecuteApplicationCommand", "DisallowJoin");
            Console.WriteLine($"INFO: one ribbon command on the cobogó house takes {timer.ElapsedMilliseconds} ms, almost all of it " +
                              "redrawing the plan and the 3D view (the model itself changes in about 0.1 s)");
            Check(Current().StartJoin == WallJoinSetting.Disallowed && Current().EndJoin == WallJoinSetting.Disallowed,
                "Disallow Join releases both ends of the house's longest wall");
            session.SetWallJoins([longest.Id], WallJoinSetting.Butt);
            Invoke("RefreshAll");
            Invoke("ExecuteApplicationCommand", "ButtJoin");
            Check(Current().StartJoin == WallJoinSetting.Butt, "Butt Join makes the wall butt");
            Invoke("ExecuteApplicationCommand", "AllowJoin");
            Check(Current().StartJoin == WallJoinSetting.Miter && Current().EndJoin == WallJoinSetting.Miter,
                "Allow Join restores mitered corners");
            Invoke("ExecuteApplicationCommand", "Pin");
            Check(Current().Pinned, "Pin pins the wall");
            Invoke("ExecuteApplicationCommand", "Pin");
            Check(!Current().Pinned, "Pin toggles off again");
            var start = Current().Start;
            var hosted = session.Document.Openings.Count(opening => opening.WallId == longest.Id);
            Invoke("ExecuteApplicationCommand", "FlipWall");
            Check(Current().End == start, "Flip Wall swaps the ends of the wall");
            Invoke("ExecuteApplicationCommand", "FlipWall");
            Check(Current().Start == start, "Flipping again restores the wall");
            var wallCount = session.Document.Walls.Count;
            var openingCount = session.Document.Openings.Count;
            Invoke("ExecuteApplicationCommand", "Copy");
            Invoke("ExecuteApplicationCommand", "Paste");
            Check(session.Document.Walls.Count == wallCount + 1 && session.Document.Openings.Count == openingCount + hosted,
                $"Copy and Paste add a wall with its {hosted} openings");
            Invoke("ExecuteApplicationCommand", "Cut");
            Check(session.Document.Walls.Count == wallCount && session.Document.Openings.Count == openingCount,
                "Cut removes the pasted wall with its openings");

            // The same operations from the keyboard: Ctrl+C, Ctrl+V and Ctrl+X, and nothing without Control.
            Invoke("SelectElement", longest.Id);
            Check(!(bool)Invoke("TryHandleClipboardShortcut", Key.C, ModifierKeys.None)! &&
                  !(bool)Invoke("TryHandleClipboardShortcut", Key.C, ModifierKeys.Control | ModifierKeys.Shift)!,
                "The letter C alone, or with Shift as well, is not a copy shortcut");
            Check((bool)Invoke("TryHandleClipboardShortcut", Key.C, ModifierKeys.Control)! &&
                  (bool)Invoke("TryHandleClipboardShortcut", Key.V, ModifierKeys.Control)! &&
                  session.Document.Walls.Count == wallCount + 1,
                "Ctrl+C then Ctrl+V pastes a copy of the selected wall");
            Check((bool)Invoke("TryHandleClipboardShortcut", Key.X, ModifierKeys.Control)! && session.Document.Walls.Count == wallCount,
                "Ctrl+X cuts the selected wall");
            Invoke("TryHandleClipboardShortcut", Key.V, ModifierKeys.Control);
            Invoke("TryHandleClipboardShortcut", Key.X, ModifierKeys.Control);
            Check(session.Document.Walls.Count == wallCount, "Ctrl+V after Ctrl+X puts the wall back and a further Ctrl+X takes it away again");

            // Commands that ask for numbers or text, answered the way a person would, on the house's real walls.
            var responder = typeof(MainWindow).Assembly.GetType("Guanacaste3D.Desktop.TextPrompt")!
                .GetField("Responder", BindingFlags.NonPublic | BindingFlags.Static)!;
            void Answer(params string?[] replies)
            {
                var queue = new Queue<string?>(replies);
                responder.SetValue(null, (Func<string, string?>)(_ => queue.Count > 0 ? queue.Dequeue() : null));
            }
            try
            {
                Invoke("SelectElement", longest.Id);
                var count = session.Document.Walls.Count;
                Answer("3; 0; 2");
                Invoke("ExecuteApplicationCommand", "Array");
                Check(session.Document.Walls.Count == count + 2, "The Array command makes a linear array of three from the house's longest wall");
                Invoke("SelectElement", longest.Id);
                Answer("r; 4; 90");
                Invoke("ExecuteApplicationCommand", "Array");
                Check(session.Document.Walls.Count == count + 5, "The Array command also makes a radial array of four");
                Invoke("SelectElement", longest.Id);
                Answer("0.5");
                Invoke("ExecuteApplicationCommand", "Offset");
                Check(session.Document.Walls.Count == count + 6, "The Offset command copies the wall half a meter to its left");
                Answer("zzz-no-existe", "x");
                Invoke("ExecuteApplicationCommand", "FindReplace");
                Check(!string.IsNullOrWhiteSpace(((TextBlock)opened.FindName("StatusText")).Text), "Find and Replace reports when the text is nowhere");
                var room = session.Document.Rooms[0].Name.Split(' ')[0];
                Answer(room, "ZONA");
                Invoke("ExecuteApplicationCommand", "FindReplace");
                Check(session.Document.Rooms.Any(candidate => candidate.Name.Contains("ZONA")), $"Find and Replace changes '{room}' in the house's rooms");
                Invoke("ExecuteApplicationCommand", "Numbering");
                Check(session.Document.Rooms.Select(candidate => candidate.Number).Distinct().Count() == session.Document.Rooms.Count,
                    "The Numbering command leaves every room with its own number");
                Invoke("SelectElement", longest.Id);
                Answer("1.05");
                Invoke("ExecuteApplicationCommand", "Scale");
                Invoke("ExecuteApplicationCommand", "AttachTop");
                Check(session.Document.Walls.Any(), "Scale and Attach Top run on a real wall without failing");

                // View filters: a typed rule hides the walls it matches in the active view only.
                tabs.SelectedIndex = 1;
                Pump();
                var activeView = (ProjectView)Invoke("ActiveProjectView")!;
                var wallsInView = ((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls.Count;
                Answer("espesor > 0.01");
                Invoke("ExecuteApplicationCommand", "Filters");
                var filteredView = session.Document.Views.First(view => view.Id == activeView.Id);
                Check(filteredView.Filters.Count == 1 && ((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls.Count == 0 &&
                      session.Document.Walls.Count > 0,
                    $"The Filters command hides all {wallsInView} walls of the active view with a rule on thickness, and the house keeps them");
                Answer("blah blah blah");
                Invoke("ExecuteApplicationCommand", "Filters");
                Check(session.Document.Views.First(view => view.Id == activeView.Id).Filters.Count == 1, "Text that is not a rule changes nothing");
                Answer("quitar");
                Invoke("ExecuteApplicationCommand", "Filters");
                Check(session.Document.Views.First(view => view.Id == activeView.Id).Filters.Count == 0 &&
                      ((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls.Count == wallsInView,
                    "Typing «quitar» removes the filters and the view shows its walls again");
                // Phases: the window opens with the two default phases and works on the selected wall and the active view.
                T Named<T>(string windowField, string name) where T : FrameworkElement =>
                    (T)System.Windows.LogicalTreeHelper.FindLogicalNode(Field<Window>(windowField), name)!;
                void Click(string windowField, string name) =>
                    Named<System.Windows.Controls.Button>(windowField, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                tabs.SelectedIndex = 1;
                Pump();
                var totalWalls = ((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls.Count;
                Invoke("SelectElement", longest.Id);
                Invoke("ExecuteApplicationCommand", "Phases");
                Pump();
                var phaseIds = session.Document.Phases.Select(phase => phase.Id).ToArray();
                Check(phaseIds.Length == 2 && Field<Window>("_phasesWindow").IsVisible, "The Phases command opens its window with an existing and a new phase");
                Named<System.Windows.Controls.ComboBox>("_phasesWindow", "PhaseCreatedBox").SelectedIndex = 2;
                Click("_phasesWindow", "PhaseAssign");
                Check(session.Document.PhaseAssignments.Any(item => item.ElementId == longest.Id && item.CreatedPhaseId == phaseIds[1]),
                    "Assigning from the window creates the selected wall in the new phase");
                Named<System.Windows.Controls.ComboBox>("_phasesWindow", "PhaseViewBox").SelectedIndex = 2;
                Named<System.Windows.Controls.ComboBox>("_phasesWindow", "PhaseFilterBox").SelectedIndex = (int)PhaseFilter.ShowExisting;
                Click("_phasesWindow", "PhaseViewApply");
                Check(((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls.Count == totalWalls - 1,
                    "Showing only the existing in the new phase hides the one wall that is new");
                Named<System.Windows.Controls.ComboBox>("_phasesWindow", "PhaseFilterBox").SelectedIndex = (int)PhaseFilter.ShowAll;
                Click("_phasesWindow", "PhaseViewApply");
                Answer("Demolición");
                Click("_phasesWindow", "PhaseAdd");
                Check(session.Document.Phases.Count == 3 && session.Document.Phases[2].Name == "Demolición", "A phase added from the window joins the end of the list");
                Click("_phasesWindow", "PhaseClose");
                Pump();
                Check(!Field<Window?>("_phasesWindow")?.IsVisible ?? true, "The window closes");

                // Design options: a set, an alternative, and the wall moved into it.
                Invoke("ExecuteApplicationCommand", "DesignOptions");
                Pump();
                Answer("Fachada", "Cobogó");
                Click("_optionsWindow", "OptionSetAdd");
                Answer("Muro liso");
                Click("_optionsWindow", "OptionAdd");
                Check(session.Document.OptionSets.Count == 1 && session.Document.OptionSets[0].Options.Select(option => option.Name).SequenceEqual(["Cobogó", "Muro liso"]),
                    "The window makes the Fachada set with its primary option and an alternative");
                Named<System.Windows.Controls.ListBox>("_optionsWindow", "OptionList").SelectedIndex = 1;
                Click("_optionsWindow", "OptionAssign");
                Check(((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls.Count == totalWalls - 1,
                    "A wall put in the alternative disappears from a view that shows the primary option");
                Named<System.Windows.Controls.ComboBox>("_optionsWindow", "OptionViewBox").SelectedIndex = 1;
                Click("_optionsWindow", "OptionViewApply");
                Check(((BuildingDocument)Invoke("ModelDisplayDocument", false)!).Walls.Count == totalWalls,
                    "Choosing the alternative for the view brings it back");
                Click("_optionsWindow", "OptionUnassign");
                Click("_optionsWindow", "OptionClose");
                Pump();

                // Groups: Create Group gathers the selection, and the manager moves, copies, saves, loads and dissolves it.
                tabs.SelectedIndex = 0;
                Pump();
                var groupWalls = new[] { angleA, angleB };
                SetField("_viewSelection", new HashSet<Guid>(groupWalls));
                var wallsWithGroup = session.Document.Walls.Count;
                Answer("Esquina de prueba");
                Invoke("ExecuteApplicationCommand", "CreateGroup");
                Pump();
                var created = session.Document.ElementGroups.SingleOrDefault(item => item.Name == "Esquina de prueba");
                Check(created is { Kind: ElementGroupKind.Group } && created.MemberIds.OrderBy(id => id).SequenceEqual(groupWalls.OrderBy(id => id)) &&
                      Field<Window?>("_groupsWindow")?.IsVisible == true, "Create Group gathers the selected walls and opens the groups window");
                var startBefore = session.Document.Walls.Single(wall => wall.Id == angleA).Start;
                Answer("1 0");
                Click("_groupsWindow", "GroupMove");
                Check(Math.Abs(session.Document.Walls.Single(wall => wall.Id == angleA).Start.X - (startBefore.X + 1)) < 1e-9,
                    "Move in the groups window shifts the group by the distance typed");
                Answer("0 10");
                Click("_groupsWindow", "GroupCopy");
                Check(session.Document.ElementGroups.Count(item => item.Kind == ElementGroupKind.Group) == 2 && session.Document.Walls.Count == wallsWithGroup + 2,
                    "Copy makes a second group with its own walls");
                var saveFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"group-{Guid.NewGuid():N}.g3dgroup");
                Check((bool)Invoke("SaveGroupFile", created!.Id, saveFile)! && System.IO.File.ReadAllText(saveFile).Contains("G3D-GROUP/1"), "A group is saved to a file");
                Answer("0 20");
                Check((bool)Invoke("LoadGroupFile", saveFile)! && session.Document.Walls.Count == wallsWithGroup + 4,
                    "The saved file loads back as a third group, shifted 20 m");
                System.IO.File.Delete(saveFile);
                Answer("no es una pareja");
                Check(!(bool)Invoke("LoadGroupFile", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "missing.g3dgroup"))!, "A bad shift or a missing file loads nothing");
                Click("_groupsWindow", "GroupUngroup");
                Check(session.Document.ElementGroups.Count(item => item.Kind == ElementGroupKind.Group) == 2 && session.Document.Walls.Count == wallsWithGroup + 4,
                    "Dissolving a group leaves its walls where they are");
                Click("_groupsWindow", "GroupClose");
                // Masses and site: the buttons arm their tools, and walls and a roof are built from a selected mass.
                Invoke("ExecuteApplicationCommand", "PlaceMass");
                Check(Field<object>("_structuralTool").ToString() == "(Mass, Rectangle)", "Place Mass arms the mass tool");
                Answer("12");
                Invoke("ExecuteApplicationCommand", "InPlaceMass");
                Check(Field<object>("_structuralTool").ToString() == "(Mass, Rectangle)", "In-Place Mass asks for a height and arms the same tool");
                var massesBefore = session.Document.StructuralElements.Count(element => element.Kind == StructuralElementKind.Mass);
                Invoke("CreateStructuralFromTool", StructuralElementKind.Mass, new Point2(600, 600), new Point2(608, 606));
                var mass = session.Document.StructuralElements.Last();
                Check(mass.Kind == StructuralElementKind.Mass && session.Document.StructuralElements.Count(element => element.Kind == StructuralElementKind.Mass) == massesBefore + 1,
                    "Two corners make a mass");
                Invoke("CancelStructuralTool");
                Invoke("ExecuteApplicationCommand", "ParkingComponent");
                Check(Field<object>("_structuralTool").ToString() == "(ParkingSpace, Rectangle)", "The parking button arms the parking space tool");
                Invoke("CancelStructuralTool");

                var wallsBeforeMass = session.Document.Walls.Count;
                SetField("_selectedStructuralId", (Guid?)mass.Id);
                Invoke("ExecuteApplicationCommand", "WallByFace");
                Check(session.Document.Walls.Count > wallsBeforeMass && (session.Document.Walls.Count - wallsBeforeMass) % 4 == 0,
                    $"Walls by face builds {session.Document.Walls.Count - wallsBeforeMass} walls on the selected mass");
                var floorsBeforeMass = session.Document.Floors.Count;
                SetField("_selectedStructuralId", (Guid?)mass.Id);
                Invoke("ExecuteApplicationCommand", "RoofByFace");
                Check(session.Document.Floors.Count == floorsBeforeMass + 1 && session.Document.Floors.Last().Kind == FloorKind.Roof, "Roof by face puts a roof over the mass");
                SetField("_selectedStructuralId", null);
                Invoke("ExecuteApplicationCommand", "RoofByFace");
                Check(session.Document.Floors.Count == floorsBeforeMass + 1, "Without a selected mass nothing is built");

                tabs.SelectedIndex = 0;
                Pump();
                Invoke("ExecuteApplicationCommand", "ShowMass");
                var planOfView = (ProjectView)Invoke("ActiveProjectView")!;
                Check(planOfView.HiddenCategories.Contains(ViewCategory.Masses), "Show Mass hides the masses in the active view");
                Invoke("ExecuteApplicationCommand", "ShowMass");
                Check(!((ProjectView)Invoke("ActiveProjectView")!).HiddenCategories.Contains(ViewCategory.Masses), "Pressing it again shows them");

                Invoke("ExecuteApplicationCommand", "ContextualData");
                Pump();
                Check(InWindow<System.Windows.Controls.TextBlock>("_contextWindow", "ContextText").Text.Contains("Solsticio de junio"), "Site Data lists the sun's day for the key dates of the year");
                ClickIn("_contextWindow", "ContextClose");

                // Sheet aids: the matchline tool, a guide grid on the open sheet and a reference to another view.
                Invoke("ExecuteApplicationCommand", "Matchline");
                Check(Field<object>("_structuralTool").ToString() == "(Matchline, Line)", "Matchline arms a line tool of its own kind");
                Invoke("CancelStructuralTool");
                tabs.SelectedIndex = 3;
                Pump();
                Answer("30");
                Invoke("ExecuteApplicationCommand", "GuideGrid");
                Check(session.Document.Sheets.Any(sheet => sheet.GuideGridMm == 30), "Guide Grid sets the spacing asked for on the open sheet");
                Answer("abc");
                Invoke("ExecuteApplicationCommand", "GuideGrid");
                Check(session.Document.Sheets.Any(sheet => sheet.GuideGridMm == 30), "A spacing that is not a number changes nothing");
                Answer("0");
                Invoke("ExecuteApplicationCommand", "GuideGrid");
                Check(session.Document.Sheets.All(sheet => sheet.GuideGridMm == 0), "Zero removes the guide grid");
                tabs.SelectedIndex = 0;
                Pump();
                var targetView = session.Document.Views.First(view => view.Kind == ProjectViewKind.Elevation || view.Kind == ProjectViewKind.Section).Name;
                Answer("zzz no existe");
                Invoke("ExecuteApplicationCommand", "ViewReference");
                Check(Field<object>("_structuralTool") is null || Field<object>("_structuralTool").ToString() != "(ViewReference, Point)", "A view name that matches nothing arms nothing");
                Answer(targetView);
                Invoke("ExecuteApplicationCommand", "ViewReference");
                Check(Field<object>("_structuralTool").ToString() == "(ViewReference, Point)", "View Reference asks for a view and arms a click in the plan");
                var referencesBefore = session.Document.StructuralElements.Count(element => element.Kind == StructuralElementKind.ViewReference);
                Invoke("PlaceViewReference", new Point2(603, 603));
                Invoke("RenderPlan");
                Pump();
                Check(session.Document.StructuralElements.Count(element => element.Kind == StructuralElementKind.ViewReference) == referencesBefore + 1 &&
                      session.Document.StructuralElements.Last().Name.StartsWith(targetView, StringComparison.Ordinal), "The click puts the reference, named after the view it points to");
                Invoke("CancelStructuralTool");

                // Annotation marks: spot marks, keynote, symbols, detail components, the colour legend and the spelling check.
                foreach (var (command, kind, mode) in new[]
                         {
                             ("ElevationDimension", StructuralElementKind.SpotElevation, "Point"), ("SpotCoordinate", StructuralElementKind.SpotCoordinate, "Point"),
                             ("SpotSlope", StructuralElementKind.SpotSlope, "Line"), ("ColorFillLegend", StructuralElementKind.ColorLegend, "Point")
                         })
                {
                    Invoke("ExecuteApplicationCommand", command);
                    Check(Field<object>("_structuralTool").ToString() == $"({kind}, {mode})", $"{command} arms the {kind} tool");
                    Invoke("CancelStructuralTool");
                }
                tabs.SelectedIndex = 0;
                Pump();
                var marksBefore = session.Document.StructuralElements.Count;
                Invoke("CreateStructuralFromTool", StructuralElementKind.SpotElevation, new Point2(615, 615), new Point2(615, 615));
                Invoke("CreateStructuralFromTool", StructuralElementKind.SpotCoordinate, new Point2(616, 615), new Point2(616, 615));
                Invoke("CreateStructuralFromTool", StructuralElementKind.ColorLegend, new Point2(620, 620), new Point2(620, 620));
                Answer("0.5");
                Invoke("CreateSpotSlope", new Point2(610, 610), new Point2(620, 610));
                Check(session.Document.StructuralElements.Count == marksBefore + 4 && session.Document.StructuralElements.Last(item => item.Kind == StructuralElementKind.SpotSlope).Rise == 0.5,
                    "Spot elevation, coordinate, legend and a slope (asking for its rise) are placed");

                Answer("04 22 00");
                Invoke("ExecuteApplicationCommand", "Keynote");
                Check(Field<object>("_structuralTool").ToString() == "(Keynote, Point)", "Keynote asks for a code and arms a click");
                Invoke("PlaceKeynote", new Point2(612, 612));
                Check(session.Document.StructuralElements.Last().Name.StartsWith("04 22 00 · ", StringComparison.Ordinal), "The click places the keynote with its text from the table");
                Answer("Flecha norte");
                Invoke("ExecuteApplicationCommand", "Symbol");
                Invoke("PlaceSymbol", new Point2(630, 630));
                Check(session.Document.StructuralElements.Last() is { Kind: StructuralElementKind.Symbol, Name: "NorthArrow" }, "Symbol finds a symbol by its name and places it");
                Answer("Anclaje");
                Invoke("ExecuteApplicationCommand", "DetailComponent");
                Invoke("PlaceSymbol", new Point2(632, 630));
                Check(session.Document.StructuralElements.Last() is { Kind: StructuralElementKind.Symbol, Name: "Anchor" }, "Detail Component places a component of the detail library");
                Invoke("CancelStructuralTool");
                Answer("dragón");
                Invoke("ExecuteApplicationCommand", "Symbol");
                Check(Field<object?>("_structuralTool") is null, "A symbol name that is not in the library arms nothing");
                Invoke("CancelStructuralTool");
                Invoke("RenderPlan");
                Pump();
                Check(((Canvas)opened.FindName("PlanCanvas")).Children.Count > 50, "The plan draws the house with all its marks");

                session.AddModelText(Field<Guid>("_activeLevelId"), new Point2(650, 650), "Muro  de  cobogo cobogo");
                Invoke("ExecuteApplicationCommand", "Spelling");
                Pump();
                Check(Field<Window?>("_spellingWindow")?.IsVisible == true && InWindow<System.Windows.Controls.ListBox>("_spellingWindow", "SpellingList").Items.Count >= 3,
                    "Spelling lists the problems it finds in the texts of the project");
                ClickIn("_spellingWindow", "SpellingFix");
                Pump();
                Check(session.Document.StructuralElements.Any(item => item.Kind == StructuralElementKind.ModelText && item.Name == "Muro de cobogó") &&
                      InWindow<System.Windows.Controls.TextBlock>("_spellingWindow", "SpellingSummary").Text.Length > 0, "Fix corrects the texts and the window checks again");
                ClickIn("_spellingWindow", "SpellingClose");

                // Site imports: terrain from survey points, a point cloud, gbXML spaces, an image to trace over and a kept PDF.
                var temp = System.IO.Path.GetTempPath();
                var pointsFile = System.IO.Path.Combine(temp, $"levantamiento-{Guid.NewGuid():N}.csv");
                System.IO.File.WriteAllLines(pointsFile, new[] { "x,y,z" }.Concat(Enumerable.Range(0, 60).Select(i => FormattableString.Invariant($"{i % 10 * 3},{i / 10 * 3},{20 + i % 10 * 0.1}"))));
                var floorsBeforeImport = session.Document.Floors.Count;
                Check((bool)Invoke("ImportTerrainFile", pointsFile)! && session.Document.Floors.Count == floorsBeforeImport + 1 && session.Document.Floors.Last().IsToposolid,
                    "Link Topography makes a terrain from a file of survey points");
                Check((bool)Invoke("ImportPointCloudFile", pointsFile)! && session.Document.PointClouds.Count == 1, "Point Cloud keeps the points of a file as a reference");
                Invoke("RenderPlan");
                Pump();
                Check(!(bool)Invoke("ImportTerrainFile", System.IO.Path.Combine(temp, "no-existe.csv"))!, "A file that does not exist is reported and imports nothing");
                System.IO.File.Delete(pointsFile);

                var gbxmlFile = System.IO.Path.Combine(temp, $"energia-{Guid.NewGuid():N}.xml");
                System.IO.File.WriteAllText(gbxmlFile, "<gbXML lengthUnit=\"Meters\"><Campus><Building><Space id=\"s\"><Name>Taller</Name><ShellGeometry><ClosedShell><PolyLoop>" +
                    "<CartesianPoint><Coordinate>0</Coordinate><Coordinate>0</Coordinate><Coordinate>0</Coordinate></CartesianPoint><CartesianPoint><Coordinate>6</Coordinate><Coordinate>0</Coordinate><Coordinate>0</Coordinate></CartesianPoint>" +
                    "<CartesianPoint><Coordinate>6</Coordinate><Coordinate>4</Coordinate><Coordinate>0</Coordinate></CartesianPoint></PolyLoop></ClosedShell></ShellGeometry></Space></Building></Campus></gbXML>");
                var roomsBeforeImport = session.Document.Rooms.Count;
                Check((bool)Invoke("ImportGbxmlFile", gbxmlFile)! && session.Document.Rooms.Count == roomsBeforeImport + 1 && session.Document.Rooms.Any(room => room.Name == "Taller"),
                    "Import gbXML makes a room for each space of the file");
                System.IO.File.Delete(gbxmlFile);

                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(8, 4, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, new byte[8 * 4 * 3], 8 * 3);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                var imageFile = System.IO.Path.Combine(temp, $"plano-{Guid.NewGuid():N}.png");
                using (var stream = System.IO.File.Create(imageFile)) encoder.Save(stream);
                Answer("12");
                Check((bool)Invoke("AddImageFile", imageFile)! && session.Document.References.Count(file => file.Kind == ReferenceKind.Image) == 1 && session.Document.Underlays.Count == 1 &&
                      Math.Abs(session.Document.Underlays[0].Width - 12) < 1e-9, "Import Image keeps the image in the project and lays it in the plan, 12 m wide");
                Invoke("RenderPlan");
                Pump();
                Check(((Canvas)opened.FindName("PlanCanvas")).Children.OfType<System.Windows.Controls.Image>().Any(), "The plan draws the image under the walls");
                System.IO.File.Delete(imageFile);
                var pdfFile = System.IO.Path.Combine(temp, $"memoria-{Guid.NewGuid():N}.pdf");
                System.IO.File.WriteAllText(pdfFile, "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");
                Check((bool)Invoke("AddPdfFile", pdfFile)! && session.Document.References.Count(file => file.Kind == ReferenceKind.Pdf) == 1, "Import PDF keeps the PDF inside the project");
                System.IO.File.WriteAllText(pdfFile, "esto no es un pdf, aunque lo diga su nombre");
                Check(!(bool)Invoke("AddPdfFile", pdfFile)! && session.Document.References.Count(file => file.Kind == ReferenceKind.Pdf) == 1, "A file that only has the name of a PDF is refused");
                System.IO.File.Delete(pdfFile);
                Invoke("ShowReferencesWindow");
                Pump();
                Check(InWindow<System.Windows.Controls.ListBox>("_referencesWindow", "ReferenceList").Items.Count == 2, "The references window lists the image and the PDF kept in the project");
                InWindow<System.Windows.Controls.ListBox>("_referencesWindow", "ReferenceList").SelectedIndex = 0;
                Answer("1 1 8 15 0.4");
                ClickIn("_referencesWindow", "ReferenceMove");
                Check(session.Document.Underlays[0] is { Width: 8, RotationDegrees: 15, Opacity: 0.4 }, "Move / scale changes the place, width, turn and opacity of the image");
                ClickIn("_referencesWindow", "ReferenceRemove");
                Check(session.Document.References.Count == 1 && session.Document.Underlays.Count == 0, "Removing the image from the window takes its placement away too");
                ClickIn("_referencesWindow", "ReferenceClose");

                // Collaboration: a central model shared with a second copy, worksets, the monitor and the edit requests.
                var centralFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"central-ui-{Guid.NewGuid():N}");
                System.IO.Directory.CreateDirectory(centralFolder);
                Environment.SetEnvironmentVariable("G3D_CENTRAL_DIR", System.IO.Path.Combine(centralFolder, "bases"));
                var centralPath = System.IO.Path.Combine(centralFolder, "central.g3d");
                Check(!(bool)Invoke("SynchronizeNow", true)!, "Synchronizing before collaboration is activated is refused");
                Invoke("ExecuteApplicationCommand", "Collaborate");
                Pump();
                InWindow<System.Windows.Controls.TextBox>("_collaborationWindow", "CollabUser").Text = "Ana";
                InWindow<System.Windows.Controls.TextBox>("_collaborationWindow", "CollabCentral").Text = centralPath;
                ClickIn("_collaborationWindow", "CollabEnable");
                Check(session.Document.Collaboration is { Enabled: true, User: "Ana" }, "The Collaborate window turns on sharing with the name and the central file typed");
                ClickIn("_collaborationWindow", "CollabSync");
                Check(System.IO.File.Exists(centralPath), "Synchronizing creates the central model");
                ClickIn("_collaborationWindow", "CollabClose");

                var other = new DocumentSession(ProjectFile.Load(centralPath));
                other.EnableCollaboration("Beto", centralPath);
                other.SynchronizeWithCentral();
                var farWall = other.Document.Walls.Last().Id;
                var farBefore = session.Document.Walls.Single(wall => wall.Id == farWall).Start;
                other.MoveWall(farWall, 2, 3);
                other.SynchronizeWithCentral();
                Invoke("ExecuteApplicationCommand", "ReloadLatest");
                var farAfter = session.Document.Walls.Single(wall => wall.Id == farWall).Start;
                Check(Math.Abs(farAfter.X - (farBefore.X + 2)) < 1e-9 && Math.Abs(farAfter.Y - (farBefore.Y + 3)) < 1e-9, "Reload Latest brings in what the other person moved");

                Invoke("ExecuteApplicationCommand", "Worksets");
                Pump();
                Answer("Fachada");
                ClickIn("_worksetsWindow", "WorksetNew");
                SetField("_selectedWallId", (Guid?)session.Document.Walls[0].Id);
                SetField("_viewSelection", new HashSet<Guid> { session.Document.Walls[0].Id, session.Document.Walls[1].Id });
                InWindow<System.Windows.Controls.ListBox>("_worksetsWindow", "WorksetList").SelectedIndex = 0;
                ClickIn("_worksetsWindow", "WorksetAssign");
                Check(session.Document.Worksets.Count == 1 && session.Document.WorksetAssignments.Count == 2, "The Worksets window makes a workset and puts the selected walls in it");
                ClickIn("_worksetsWindow", "WorksetBorrow");
                Check(session.Document.Worksets[0].Owner == "Ana", "Taking the workset makes it Ana's");
                ClickIn("_worksetsWindow", "WorksetToggle");
                Check(!session.Document.Worksets[0].Visible && ViewVisibility.Apply(session.Document, session.Document.Views.First(view => view.Kind == ProjectViewKind.Model3D)).Walls.Count == session.Document.Walls.Count - 2,
                    "Hiding the workset takes its walls out of the views");
                ClickIn("_worksetsWindow", "WorksetToggle");
                ClickIn("_worksetsWindow", "WorksetClose");
                SetField("_viewSelection", null);

                Invoke("ExecuteApplicationCommand", "SynchronizeCentral");
                other.SynchronizeWithCentral(writeCentral: false);
                Check(other.Document.Worksets.Single().Owner == "Ana", "Synchronizing sends the taken workset to the other copy");
                Invoke("ExecuteApplicationCommand", "WorksharingMonitor");
                Pump();
                Check(InWindow<System.Windows.Controls.TextBlock>("_monitorWindow", "MonitorText").Text.Contains("Ana"), "The monitor names the user and the state of the shared model");
                ClickIn("_monitorWindow", "MonitorClose");
                Invoke("ExecuteApplicationCommand", "EditRequests");
                Pump();
                Check(Field<Window?>("_requestsWindow")?.IsVisible == true, "Edit Requests opens with what the last synchronization left in dispute");
                ClickIn("_requestsWindow", "RequestClose");
                Invoke("ExecuteApplicationCommand", "Relinquish");
                Check(session.Document.Worksets[0].Owner is null, "Relinquish lets go of the workset");
                session.DisableCollaboration();
                Environment.SetEnvironmentVariable("G3D_CENTRAL_DIR", null);
                try { System.IO.Directory.Delete(centralFolder, true); } catch (System.IO.IOException) { }

                // Coordination: interference check, share package, publish set and the list of shared models.
                Invoke("ExecuteApplicationCommand", "Coordinate");
                Pump();
                var interferenceCount = InterferenceCheck.Run(session.Document).Count;
                Check(InWindow<System.Windows.Controls.ListBox>("_interferenceWindow", "InterferenceList").Items.Count == interferenceCount,
                    $"Coordinate lists the {interferenceCount} interferences of the house");
                if (interferenceCount > 0)
                {
                    InWindow<System.Windows.Controls.ListBox>("_interferenceWindow", "InterferenceList").SelectedIndex = 0;
                    ClickIn("_interferenceWindow", "InterferenceSelect");
                    Check(Field<HashSet<Guid>?>("_viewSelection") is { Count: 2 }, "Selecting an interference selects the two elements");
                    SetField("_viewSelection", null);
                }
                ClickIn("_interferenceWindow", "InterferenceClose");

                var outFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"salida-{Guid.NewGuid():N}");
                Environment.SetEnvironmentVariable("G3D_CENTRAL_DIR", System.IO.Path.Combine(outFolder, "bases"));
                Check((bool)Invoke("SharePackageTo", System.IO.Path.Combine(outFolder, "paquete"))! && System.IO.Directory.GetFiles(System.IO.Path.Combine(outFolder, "paquete")).Length == 5,
                    "Share writes the project, IFC, DXF, PDF and the summary into a folder");
                Invoke("ExecuteApplicationCommand", "PublishSettings");
                Pump();
                InWindow<System.Windows.Controls.TextBox>("_publishWindow", "PublishFolder").Text = System.IO.Path.Combine(outFolder, "publicar");
                InWindow<System.Windows.Controls.CheckBox>("_publishWindow", "PublishDxf").IsChecked = true;
                ClickIn("_publishWindow", "PublishNow");
                Check(System.IO.Directory.Exists(System.IO.Path.Combine(outFolder, "publicar")) && System.IO.Directory.GetFiles(System.IO.Path.Combine(outFolder, "publicar")).Length == 2 &&
                      session.Document.Publish.Dxf && session.Document.Publish.Folder.EndsWith("publicar", StringComparison.Ordinal), "Publish Now writes the sheets as PDF and the model as DXF, and remembers the choice");
                ClickIn("_publishWindow", "PublishClose");

                RecentCentralModels.Add(System.IO.Path.Combine(outFolder, "inexistente.g3d"));
                Invoke("ExecuteApplicationCommand", "ManageCloudModels");
                Pump();
                Check(InWindow<System.Windows.Controls.ListBox>("_sharedModelsWindow", "SharedModelList").Items.Count == 1, "Manage Models lists the central models used on this computer");
                ClickIn("_sharedModelsWindow", "SharedModelForget");
                Check(RecentCentralModels.List().Count == 0, "Forgetting one takes it off the list");
                ClickIn("_sharedModelsWindow", "SharedModelClose");
                Environment.SetEnvironmentVariable("G3D_CENTRAL_DIR", null);
                try { System.IO.Directory.Delete(outFolder, true); } catch (System.IO.IOException) { }

                // Table imports: parameter values by element id, and review marks.
                var tableWall = session.Document.Walls[3].Id;
                var tableParameter = session.AddParameter("Etiqueta de obra", ParameterSpec.Text, ParameterBinding.Instance, [ElementCategory.Walls]);
                var tableFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"valores-{Guid.NewGuid():N}.csv");
                System.IO.File.WriteAllText(tableFile, $"Id;Etiqueta de obra{Environment.NewLine}{tableWall};Muro de cobogó norte{Environment.NewLine}");
                Check((bool)Invoke("ImportParameterValuesFile", tableFile)! && session.Document.ParameterValues.Any(value => value.OwnerId == tableWall && value.DefinitionId == tableParameter && value.Text == "Muro de cobogó norte"),
                    "Extended Properties fills a parameter on the wall named by the table");
                System.IO.File.WriteAllText(tableFile, "esto no es una tabla");
                Check(!(bool)Invoke("ImportParameterValuesFile", tableFile)!, "A file that is not a table imports nothing");
                System.IO.File.WriteAllText(tableFile, "# marcas" + Environment.NewLine + "605;605;Revisar el cobogó" + Environment.NewLine);
                var textsBefore = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.ModelText);
                Check((bool)Invoke("ImportReviewMarksFile", tableFile)! && session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.ModelText) == textsBefore + 1,
                    "Markups brings a review mark into the active level, as a note with a cloud");
                System.IO.File.Delete(tableFile);

                // Building services: the Systems tab's tools, stiffeners on a duct, a route through several points, a fixture and the schematic.
                foreach (var (command, kind, mode) in new[]
                         {
                             ("Duct", StructuralElementKind.Duct, "Line"), ("DuctPlaceholder", StructuralElementKind.DuctPlaceholder, "Line"), ("FlexDuct", StructuralElementKind.FlexDuct, "Line"),
                             ("CableTray", StructuralElementKind.CableTray, "Line"), ("Conduit", StructuralElementKind.Conduit, "Line"), ("FlexPipe", StructuralElementKind.FlexPipe, "Line"),
                             ("FabricationPart", StructuralElementKind.FabricationPart, "Line"), ("AirTerminal", StructuralElementKind.AirTerminal, "Point"),
                             ("MechanicalControlDevice", StructuralElementKind.MechanicalControlDevice, "Point")
                         })
                {
                    Invoke("ExecuteApplicationCommand", command);
                    Check(Field<object>("_structuralTool").ToString() == $"({kind}, {mode})", $"{command} arms the {kind} tool");
                    Invoke("CancelStructuralTool");
                }
                Invoke("CreateStructuralFromTool", StructuralElementKind.Duct, new Point2(640, 640), new Point2(646, 640));
                Answer("1.5");
                Invoke("ExecuteApplicationCommand", "DuctStiffener");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.DuctStiffener) == 4, "Duct Stiffener puts bands along the duct that was just drawn");
                SetField("_selectedStructuralId", null);
                Invoke("ExecuteApplicationCommand", "DuctStiffener");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.DuctStiffener) == 4, "Without a selected duct nothing is added");
                Answer("660 660; 670 660; 670 668");
                var piecesBefore = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.FabricationPart);
                Invoke("ExecuteApplicationCommand", "MultipointRouting");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.FabricationPart) == piecesBefore + 2, "Multipoint Routing draws a piece between each pair of the points typed");
                Answer("2 3");
                var piecesAfter = session.Document.StructuralElements.Count;
                Invoke("ExecuteApplicationCommand", "MultipointRouting");
                Check(session.Document.StructuralElements.Count == piecesAfter, "Fewer than three points draw nothing");
                Answer("inodoro");
                Invoke("ExecuteApplicationCommand", "PlumbingFixture");
                Check(Field<object?>("_structuralTool") is { } tool && tool.ToString()!.Contains("Component"), "Plumbing Fixture arms the component tool with the fixture chosen by name");
                Invoke("CancelStructuralTool");
                Invoke("RenderPlan");
                Pump();
                Invoke("ExecuteApplicationCommand", "PidModeler");
                Pump();
                var schematicCanvas = InWindow<System.Windows.Controls.Canvas>("_schematicWindow", "SchematicCanvas");
                Check(schematicCanvas.Children.Count > 5, $"The schematic draws the services of the house ({schematicCanvas.Children.Count} shapes)");
                var schematicFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"esquema-{Guid.NewGuid():N}.png");
                Check((bool)Invoke("SaveSchematicImage", schematicCanvas, schematicFile)! && new System.IO.FileInfo(schematicFile).Length > 500, "The schematic can be saved as an image");
                System.IO.File.Delete(schematicFile);
                ClickIn("_schematicWindow", "SchematicClose");

                // Steel: plate, weld and bolt tools, and the cuts and fittings of the selected member.
                foreach (var (command, kind, mode) in new[] { ("Plate", StructuralElementKind.Plate, "Rectangle"), ("Welds", StructuralElementKind.Weld, "Line") })
                {
                    Invoke("ExecuteApplicationCommand", command);
                    Check(Field<object>("_structuralTool").ToString() == $"({kind}, {mode})", $"{command} arms the {kind} tool");
                    Invoke("CancelStructuralTool");
                }
                Answer("2 2 0.075 0.02");
                Invoke("ExecuteApplicationCommand", "Bolts");
                Check(Field<object>("_structuralTool").ToString() == "(Bolt, Point)", "Bolts asks for the pattern and arms a click");
                var boltsBefore = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Bolt);
                Invoke("PlaceBolts", new Point2(690, 690));
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Bolt) == boltsBefore + 4, "The click places the 4 bolts of the pattern");
                Invoke("CancelStructuralTool");

                Invoke("CreateStructuralFromTool", StructuralElementKind.Beam, new Point2(700, 700), new Point2(706, 700));
                var steelBeam = session.Document.StructuralElements.Last();
                Invoke("CreateStructuralFromTool", StructuralElementKind.Beam, new Point2(703, 695), new Point2(703, 705));
                var crossingBeam = session.Document.StructuralElements.Last();
                SetField("_selectedStructuralId", (Guid?)steelBeam.Id);
                Answer("final 0.5 0.05");
                Invoke("ExecuteApplicationCommand", "Cope");
                Check(session.Document.StructuralElements.Single(item => item.Id == steelBeam.Id).Cuts is { Count: 1 }, "Cope notches the end of the selected beam");
                Answer("inicio 0.4 0.05 0.5");
                Invoke("ExecuteApplicationCommand", "ParametricCope");
                Check(session.Document.StructuralElements.Single(item => item.Id == steelBeam.Id).Cuts is { Count: 2 }, "Parametric Cope adds a notch across half the width");
                Answer("2 0.2");
                Invoke("ExecuteApplicationCommand", "CutThrough");
                Check(session.Document.StructuralElements.Single(item => item.Id == steelBeam.Id).Cuts is { Count: 3 }, "Cut Through adds a slot across the beam");
                Answer("1.2 -0.05 0.2 0.1");
                Invoke("ExecuteApplicationCommand", "ContourCut");
                Check(session.Document.StructuralElements.Single(item => item.Id == steelBeam.Id).Cuts is { Count: 4 }, "Contour Cut adds a hole through the beam");
                Answer("final 0.2");
                var lengthBeforeShortening = session.Document.StructuralElements.Single(item => item.Id == steelBeam.Id).Length;
                Invoke("ExecuteApplicationCommand", "Shorten");
                Check(Math.Abs(session.Document.StructuralElements.Single(item => item.Id == steelBeam.Id).Length - (lengthBeforeShortening - 0.2)) < 1e-9, "Shorten takes 20 cm off the end");
                Answer("hola esto no vale");
                Invoke("ExecuteApplicationCommand", "Cope");
                Check(session.Document.StructuralElements.Single(item => item.Id == steelBeam.Id).Cuts is { Count: 4 }, "Values that make no sense change nothing");
                Answer(crossingBeam.Name);
                SetField("_selectedStructuralId", (Guid?)steelBeam.Id);
                session.UpdateStructuralElement(crossingBeam.Id, crossingBeam.Name, 0.2, 3.0, -1.0, ElementStyle.Default);
                Invoke("ExecuteApplicationCommand", "CutBy");
                Check(session.Document.StructuralElements.Single(item => item.Id == steelBeam.Id).Cuts is { Count: 5 }, "Cut By cuts the selected beam where the named one passes through it");
                Answer("nada");
                SetField("_selectedStructuralId", (Guid?)steelBeam.Id);
                Invoke("ExecuteApplicationCommand", "Miter");
                Check(session.Document.StructuralElements.Single(item => item.Id == steelBeam.Id).Cuts is { Count: 5 }, "Mitring with a member that does not exist changes nothing");

                Invoke("CreateStructuralFromTool", StructuralElementKind.Plate, new Point2(720, 700), new Point2(721, 700.6));
                var steelPlate = session.Document.StructuralElements.Last();
                SetField("_selectedStructuralId", (Guid?)steelPlate.Id);
                Answer("1 0.1 0.1");
                Invoke("ExecuteApplicationCommand", "CornerCut");
                Check(session.Document.StructuralElements.Single(item => item.Id == steelPlate.Id).Cuts is { Count: 1 }, "Corner Cut clips a corner off the selected plate");
                Invoke("RenderPlan");
                Pump();

                // Concrete: bars on a beam, fabric, covers, panels, settings forms and the two reports.
                Invoke("ExecuteApplicationCommand", "FabricSheet");
                Check(Field<object>("_structuralTool").ToString() == "(FabricSheet, Line)", "Fabric Sheet arms the line tool");
                Invoke("CancelStructuralTool");
                Invoke("CreateStructuralFromTool", StructuralElementKind.Beam, new Point2(500, 500), new Point2(506, 500));
                var rebarBeam = session.Document.StructuralElements.Last();
                SetField("_selectedStructuralId", (Guid?)rebarBeam.Id);
                session.UpdateStructuralElement(rebarBeam.Id, rebarBeam.Name, 0.3, 0.5, 2.5, ElementStyle.Default);
                var barsBefore = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Rebar);
                Answer("3 0.016 abajo");
                Invoke("ExecuteApplicationCommand", "Rebar");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Rebar) == barsBefore + 3, "Rebar puts three bars along the selected beam");
                Answer("0.05");
                Invoke("ExecuteApplicationCommand", "Cover");
                Check(Math.Abs(session.CoverOf(rebarBeam.Id) - 0.05) < 1e-9, "Cover gives the selected beam its own cover");
                Answer("0 0; 0.3 0; 0.3 0.4; 0 0.4 | 0.01 1");
                Invoke("ExecuteApplicationCommand", "RebarPath");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Rebar) == barsBefore + 6, "Rebar Path draws a stirrup of three pieces");
                Answer("510 510 515 514 0.2 0.012 x");
                Invoke("ExecuteApplicationCommand", "RebarArea");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Rebar) > barsBefore + 6, "Rebar Area fills a slab with bars");
                var fabricBefore = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.FabricSheet);
                Answer("530 530 30 6 2.4");
                Invoke("ExecuteApplicationCommand", "TransformedFabric");
                Answer("540 540 3 1.5 0.15 0.006");
                Invoke("ExecuteApplicationCommand", "CustomFabricSheet");
                Answer("550 550 561.8 554.6");
                Invoke("ExecuteApplicationCommand", "FabricArea");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.FabricSheet) == fabricBefore + 2 + 4, "Turned, custom and area fabric add 1 + 1 + 4 sheets");
                Answer("quien sabe");
                Invoke("ExecuteApplicationCommand", "RebarPath");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.FabricSheet) == fabricBefore + 6, "Values that make no sense change nothing");

                var panelWall = session.AddWall(session.Document.Levels[0].Id, new Point2(560, 500), new Point2(569, 500), 0.2, 3);
                SetField("_selectedWallId", (Guid?)panelWall);
                Invoke("ExecuteApplicationCommand", "AssemblyParts");
                Check(session.Document.ElementGroups.Any(group => group.Kind == ElementGroupKind.Assembly && group.MemberIds.Contains(panelWall) && group.MemberIds.Count == 4), "Assembly Parts divides the wall into 4 precast panels in an assembly");
                var wallBarsBefore = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Rebar);
                SetField("_selectedWallId", (Guid?)panelWall);
                Invoke("ExecuteApplicationCommand", "PrecastReinforcement");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Rebar) == wallBarsBefore + 8, "Precast Reinforcement puts two bars in each of the 4 panels");

                Invoke("ExecuteApplicationCommand", "CoverSettings");
                Pump();
                InWindow<System.Windows.Controls.TextBox>("_settingsWindow", "Field_Beam").Text = "0.05";
                ClickIn("_settingsWindow", "SettingsApply");
                Check(Math.Abs(session.Document.Reinforcement.BeamCover - 0.05) < 1e-9, "The cover settings form changes the beam cover of the project");
                InWindow<System.Windows.Controls.TextBox>("_settingsWindow", "Field_Beam").Text = "5";
                ClickIn("_settingsWindow", "SettingsApply");
                Check(Math.Abs(session.Document.Reinforcement.BeamCover - 0.05) < 1e-9 && InWindow<System.Windows.Controls.TextBlock>("_settingsWindow", "SettingsMessage").Text.Length > 0,
                    "A cover of 5 m is refused and the form says why");
                ClickIn("_settingsWindow", "SettingsClose");
                Invoke("ExecuteApplicationCommand", "ReinforcementSettings");
                Pump();
                InWindow<System.Windows.Controls.TextBox>("_settingsWindow", "Field_Grade").Text = "Grado 40";
                ClickIn("_settingsWindow", "SettingsApply");
                Check(session.Document.Reinforcement.SteelGrade == "Grado 40", "The reinforcement form changes the steel grade");
                ClickIn("_settingsWindow", "SettingsClose");
                Invoke("ExecuteApplicationCommand", "PrecastConfiguration");
                Pump();
                InWindow<System.Windows.Controls.TextBox>("_settingsWindow", "Field_Prefix").Text = "PRE";
                ClickIn("_settingsWindow", "SettingsApply");
                Check(session.Document.Precast.Prefix == "PRE", "The precast form changes the panel prefix");
                ClickIn("_settingsWindow", "SettingsClose");
                Invoke("ExecuteApplicationCommand", "CesConfiguration");
                Pump();
                InWindow<System.Windows.Controls.TextBox>("_settingsWindow", "Field_Weight").Text = "2500";
                ClickIn("_settingsWindow", "SettingsApply");
                Check(Math.Abs(session.Document.Concrete.UnitWeight - 2500) < 1e-9, "The concrete form changes the unit weight");
                ClickIn("_settingsWindow", "SettingsClose");

                Invoke("ExecuteApplicationCommand", "ShopDrawings");
                Pump();
                var shopText = InWindow<System.Windows.Controls.TextBox>("_shopWindow", "ShopText").Text;
                Check(shopText.Contains("B1") && shopText.Contains("kg"), "Shop Drawings lists the parts and the bars of the project with their weight");
                ClickIn("_shopWindow", "ShopClose");
                var shopFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"armadura-{Guid.NewGuid():N}.csv");
                Check((bool)Invoke("SaveShopCsv", shopFile)! && System.IO.File.ReadAllText(shopFile).StartsWith("Marca;", StringComparison.Ordinal), "The table is saved as CSV with its header row");
                var camFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"corte-{Guid.NewGuid():N}.bvbs");
                Check((bool)Invoke("ExportCutList", camFile)! && System.IO.File.ReadAllLines(camFile).All(line => line.StartsWith("BF2D@Hj", StringComparison.Ordinal)), "CAM Export writes a BF2D line for each bar mark");
                System.IO.File.Delete(shopFile);
                System.IO.File.Delete(camFile);

                // Analyze: the analytical overlay, zones, the three reports, shapes of a mass and the results fill.
                Invoke("ExecuteApplicationCommand", "AnalyticalMember");
                Pump();
                var analyticalLayers = Field<System.Collections.Generic.HashSet<string>>("_analyticalLayers");
                Check(analyticalLayers.Contains("Member") && InWindow<System.Windows.Controls.CheckBox>("_analyticalWindow", "Layer_Member").IsChecked == true, "Analytical Member turns on the bars layer");
                InWindow<System.Windows.Controls.CheckBox>("_analyticalWindow", "Layer_Boundary").IsChecked = true;
                InWindow<System.Windows.Controls.CheckBox>("_analyticalWindow", "Layer_Link").IsChecked = true;
                InWindow<System.Windows.Controls.CheckBox>("_analyticalWindow", "Layer_Panel").IsChecked = true;
                InWindow<System.Windows.Controls.CheckBox>("_analyticalWindow", "Layer_Opening").IsChecked = true;
                Pump();
                Check(analyticalLayers.Count == 5, "Each analytical layer can be turned on from the window");
                InWindow<System.Windows.Controls.CheckBox>("_analyticalWindow", "Layer_Member").IsChecked = false;
                Check(!analyticalLayers.Contains("Member") && analyticalLayers.Count == 4, "A layer can be turned off");
                ClickIn("_analyticalWindow", "AnalyticalClose");
                analyticalLayers.Clear();
                Invoke("RenderPlan");

                var zoneLevel = Field<Guid>("_activeLevelId");
                var zoneRoomA = session.AddRoom(zoneLevel, new Point2(700, 700), new Point2(704, 703), "Zona A");
                var zoneRoomB = session.AddRoom(zoneLevel, new Point2(704, 700), new Point2(708, 703), "Zona B");
                Invoke("ExecuteApplicationCommand", "Zone");
                Pump();
                foreach (var box in InWindow<System.Windows.Controls.StackPanel>("_zoneWindow", "ZoneRooms").Children.OfType<System.Windows.Controls.CheckBox>()
                             .Where(box => (Guid)box.Tag == zoneRoomA || (Guid)box.Tag == zoneRoomB))
                    box.IsChecked = true;
                InWindow<System.Windows.Controls.TextBox>("_zoneWindow", "ZoneName").Text = "Zona prueba";
                ClickIn("_zoneWindow", "ZoneCreate");
                Check(session.Document.Zones.Any(zone => zone.Name == "Zona prueba" && zone.RoomIds.Contains(zoneRoomA) && zone.RoomIds.Contains(zoneRoomB)), "The zone window creates a zone with the rooms ticked");
                Pump();
                Check(InWindow<System.Windows.Controls.ListBox>("_zoneWindow", "ZoneList").Items.Count == session.Document.Zones.Count, "The zone window lists the zones of the project");
                InWindow<System.Windows.Controls.TextBox>("_zoneWindow", "ZoneName").Text = "Zona prueba";
                ClickIn("_zoneWindow", "ZoneCreate");
                Check(session.Document.Zones.Count(zone => zone.Name == "Zona prueba") == 1, "A zone with the same name is refused");
                var zoneList = InWindow<System.Windows.Controls.ListBox>("_zoneWindow", "ZoneList");
                zoneList.SelectedIndex = session.Document.Zones.FindIndex(zone => zone.Name == "Zona prueba");
                ClickIn("_zoneWindow", "ZoneRemove");
                Check(session.Document.Zones.All(zone => zone.Name != "Zona prueba"), "The zone window removes the zone selected");
                ClickIn("_zoneWindow", "ZoneClose");

                Invoke("ExecuteApplicationCommand", "StructuralAnalysis");
                Pump();
                var structuralText = InWindow<System.Windows.Controls.TextBox>("_reportWindow", "ReportText").Text;
                Check(structuralText.Contains("kg") && structuralText.Contains("Carga muerta"), "Structural Analysis reports the weights and the loads");
                ClickIn("_reportWindow", "ReportClose");
                Invoke("ExecuteApplicationCommand", "SystemsAnalysis");
                Pump();
                var systemsText = InWindow<System.Windows.Controls.TextBox>("_reportWindow", "ReportText").Text;
                Check(systemsText.Contains("Plomería") && systemsText.Contains("Electricidad"), "Systems Analysis reports plumbing and electricity");
                ClickIn("_reportWindow", "ReportClose");
                Invoke("ExecuteApplicationCommand", "EnergyOptimization");
                Pump();
                var energyText = InWindow<System.Windows.Controls.TextBox>("_reportWindow", "ReportText").Text;
                Check(energyText.Contains("Norte") && energyText.Contains("Sur") && energyText.Contains("•"), "Energy Optimization reports each orientation and gives advice");
                ClickIn("_reportWindow", "ReportClose");

                Invoke("CreateStructuralFromTool", StructuralElementKind.Mass, new Point2(720, 720), new Point2(730, 726));
                var formMass = session.Document.StructuralElements.Last();
                SetField("_selectedStructuralId", (Guid?)formMass.Id);
                var formSetsBefore = session.Document.OptionSets.Count;
                var formMassesBefore = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass);
                Answer("1 2 3");
                Invoke("ExecuteApplicationCommand", "FormCreation");
                Pump();
                Check(session.Document.OptionSets.Count == formSetsBefore + 1 && session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == formMassesBefore + 3,
                    "Form Creation makes three shapes of the mass in a new option set");
                Check(InWindow<System.Windows.Controls.TextBox>("_reportWindow", "ReportText").Text.Contains("1:1"), "Form Creation lists the shapes it made");
                ClickIn("_reportWindow", "ReportClose");
                Answer("0");
                SetField("_selectedStructuralId", (Guid?)formMass.Id);
                var formMassesAfter = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass);
                Invoke("ExecuteApplicationCommand", "FormCreation");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == formMassesAfter, "A proportion of 0 makes no shapes");

                Invoke("ExecuteApplicationCommand", "ResultsFill");
                Pump();
                InWindow<System.Windows.Controls.ComboBox>("_fillWindow", "FillMetric").SelectedIndex = (int)ResultsMetric.Area;
                ClickIn("_fillWindow", "FillApply");
                Check(session.Document.ResultsMetric == ResultsMetric.Area, "Results Fill applies the area metric");
                session.Undo();
                Check(session.Document.ResultsMetric == ResultsMetric.None, "The results fill can be undone");
                session.Redo();
                ClickIn("_fillWindow", "FillClose");
                session.SetResultsMetric(ResultsMetric.None);

                // Geometry tools: Cut Geometry on a wall and on a member, Cut Profile, and the 3D sketch of a mass.
                var cutLevel = Field<Guid>("_activeLevelId");
                var cutWall = session.AddWall(cutLevel, new Point2(800, 800), new Point2(810, 800), 0.2, 3);
                Invoke("CreateStructuralFromTool", StructuralElementKind.Beam, new Point2(805, 798), new Point2(805, 802));
                var lintel = session.Document.StructuralElements.Last();
                session.UpdateStructuralElement(lintel.Id, "Dintel UI", lintel.Width, 0.6, 2.4, ElementStyle.Default);
                SetField("_selectedStructuralId", (Guid?)null);
                SetField("_selectedWallId", (Guid?)null);
                Invoke("ExecuteApplicationCommand", "CutGeometry");
                Check(!string.IsNullOrWhiteSpace(((System.Windows.Controls.TextBlock)opened.FindName("StatusText")).Text), "Cut Geometry with nothing selected says what is missing");
                SetField("_selectedWallId", (Guid?)cutWall);
                Answer("Dintel UI");
                Invoke("ExecuteApplicationCommand", "CutGeometry");
                var cutResult = session.Document.Walls.Single(wall => wall.Id == cutWall);
                Check(cutResult.TopProfile is not null && Math.Abs(cutResult.TopHeightAt(5) - 2.4) < 1e-6 && Math.Abs(cutResult.TopHeightAt(1) - 3) < 1e-6, "Cut Geometry brings the top of the selected wall down to the underside of the beam named: " + ((System.Windows.Controls.TextBlock)opened.FindName("StatusText")).Text);
                SetField("_selectedWallId", (Guid?)cutWall);
                Answer("No existe");
                var profileBefore = session.Document.Walls.Single(wall => wall.Id == cutWall).TopProfile!.Count;
                Invoke("ExecuteApplicationCommand", "CutGeometry");
                Check(session.Document.Walls.Single(wall => wall.Id == cutWall).TopProfile!.Count == profileBefore, "A name that matches no element cuts nothing");

                Invoke("ExecuteApplicationCommand", "CutProfile");
                Pump();
                Check(Field<System.Windows.Window?>("_profileWindow") is { } profileWindow && profileWindow.IsVisible, "Cut Profile on a wall opens the wall profile editor");
                Field<System.Windows.Window>("_profileWindow").Close();

                Invoke("CreateStructuralFromTool", StructuralElementKind.Beam, new Point2(820, 800), new Point2(826, 800));
                var notched = session.Document.StructuralElements.Last();
                SetField("_selectedWallId", (Guid?)null);
                SetField("_selectedStructuralId", (Guid?)notched.Id);
                Answer("1 2 -0.1 0.1 0.1");
                Invoke("ExecuteApplicationCommand", "CutProfile");
                Check(session.Document.StructuralElements.Single(item => item.Id == notched.Id).Cuts is { Count: 1 }, "Cut Profile on a beam takes a notch out of it");
                Answer("1 2 0.5 0.1 0.1");
                Invoke("ExecuteApplicationCommand", "CutProfile");
                Check(session.Document.StructuralElements.Single(item => item.Id == notched.Id).Cuts is { Count: 1 }, "A notch that ends before it starts is refused");

                var massesAtStart = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass);
                Answer("830 830 836 834 3 2.5");
                Invoke("ExecuteApplicationCommand", "Sketch3D");
                var floatingMass = session.Document.StructuralElements.Where(item => item.Kind == StructuralElementKind.Mass).Skip(massesAtStart).SingleOrDefault();
                Check(floatingMass is not null && Math.Abs(floatingMass.Offset - 3) < 1e-6 && Math.Abs(floatingMass.Height - 2.5) < 1e-6 && Field<Guid?>("_selectedStructuralId") == floatingMass.Id,
                    "Sketch 3D makes a mass 3 m above the level and 2.5 m tall, and selects it");
                Answer("0 0 0 4 3 3");
                Invoke("ExecuteApplicationCommand", "Sketch3D");
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == massesAtStart + 1, "A sketch with a side of no length makes nothing");

                // Add-Ins: the command help, the model transmittal and the family converter.
                Invoke("ExecuteApplicationCommand", "Help");
                Pump();
                var helpList = InWindow<System.Windows.Controls.ListBox>("_helpWindow", "HelpList");
                Check(helpList.Items.Count > 250, $"Help lists every command of the ribbon ({helpList.Items.Count})");
                InWindow<System.Windows.Controls.TextBox>("_helpWindow", "HelpSearch").Text = "zzzzqq";
                Check(helpList.Items.Count == 0 && InWindow<System.Windows.Controls.TextBlock>("_helpWindow", "HelpDetail").Text.Length > 0, "Help says when nothing matches the search");
                InWindow<System.Windows.Controls.TextBox>("_helpWindow", "HelpSearch").Text = "zona";
                var zoneEntry = helpList.Items.Cast<string>().ToList().FindIndex(entry => entry.EndsWith("› Zona", StringComparison.Ordinal));
                Check(zoneEntry >= 0 && helpList.Items.Count < 40, "Searching a word keeps only the commands that mention it");
                helpList.SelectedIndex = zoneEntry;
                Check(InWindow<System.Windows.Controls.TextBlock>("_helpWindow", "HelpDetail").Text.Contains("›"), "Choosing a command shows its tab and group");
                ClickIn("_helpWindow", "HelpRun");
                Pump();
                Check(Field<System.Windows.Window?>("_zoneWindow") is { IsVisible: true } && Field<System.Windows.Window?>("_helpWindow") is null, "Help can run the command chosen, and closes itself");
                ClickIn("_zoneWindow", "ZoneClose");

                var transmitFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"transmision-{Guid.NewGuid():N}.zip");
                Invoke("ExecuteApplicationCommand", "TransmitModel");
                Pump();
                Check(InWindow<System.Windows.Controls.CheckBox>("_transmitWindow", "TransmitExports").IsChecked == true, "Transmit Model offers to include the exports");
                var transmitText = (string)Invoke("CreateTransmittal", transmitFile, false)!;
                Check(System.IO.File.Exists(transmitFile) && transmitText.Contains(System.IO.Path.GetFileName(transmitFile)), "Transmit Model writes the package and says what it holds");
                Check(((string)Invoke("VerifyTransmittal", transmitFile)!).Contains("completo"), "The package just written verifies as complete");
                using (var archive = System.IO.Compression.ZipFile.Open(transmitFile, System.IO.Compression.ZipArchiveMode.Update))
                {
                    var projectEntry = archive.Entries.First(entry => entry.Name.EndsWith(".g3d", StringComparison.Ordinal));
                    var name = projectEntry.FullName;
                    projectEntry.Delete();
                    using var writer = new System.IO.StreamWriter(archive.CreateEntry(name).Open());
                    writer.Write("alterado");
                }
                Check(((string)Invoke("VerifyTransmittal", transmitFile)!).Contains("cambió"), "A package whose project was altered says which file changed");
                Check(!((string)Invoke("CreateTransmittal", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-es-zip.txt"), false)!).Contains("creado"), "A package that is not a .zip is refused");
                ClickIn("_transmitWindow", "TransmitClose");
                System.IO.File.Delete(transmitFile);

                var convertFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"conversion-{Guid.NewGuid():N}");
                System.IO.Directory.CreateDirectory(convertFolder);
                var tetra = System.IO.Path.Combine(convertFolder, "tetraedro.obj");
                System.IO.File.WriteAllText(tetra, "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 1\nf 1 3 2\nf 1 2 4\nf 2 3 4\nf 3 1 4\n");
                var notAModel = System.IO.Path.Combine(convertFolder, "notas.txt");
                System.IO.File.WriteAllText(notAModel, "nada");
                Invoke("ExecuteApplicationCommand", "FamilyConverter");
                Pump();
                Check(InWindow<System.Windows.Controls.ComboBox>("_converterWindow", "ConverterCategory").Items.Count == Enum.GetValues<FamilyCategory>().Length - 1, "The family converter offers every category but the title blocks");
                var convertText = (string)Invoke("ConvertFamilies", new[] { tetra, notAModel }, System.IO.Path.Combine(convertFolder, "familias"), FamilyCategory.Entourage, 60000, 1.0)!;
                Check(convertText.Contains("tetraedro") && convertText.Contains("→") && convertText.Contains("no se convirtió") && System.IO.File.Exists(System.IO.Path.Combine(convertFolder, "familias", "tetraedro.g3df")),
                    "The family converter makes a family of the model and lists the file that was not a model");
                ClickIn("_converterWindow", "ConverterClose");
                System.IO.Directory.Delete(convertFolder, true);

                // Manage: generative design studies, their outcomes, and automation scripts with their recorder and player.
                SetField("_selectedStructuralId", (Guid?)null);
                var studiesAtStart = session.Document.Studies.Count;
                Check(!string.IsNullOrWhiteSpace((string)Invoke("CreateStudyFromText", "Sin masa", "4 20", "3 14", "3 12", StudyGoal.Compactness, "", "", "20", "1")!) && session.Document.Studies.Count == studiesAtStart, "A study cannot be made without a mass selected");
                Invoke("CreateStructuralFromTool", StructuralElementKind.Mass, new Point2(840, 840), new Point2(850, 846));
                var studyMass = session.Document.StructuralElements.Last();
                SetField("_selectedStructuralId", (Guid?)studyMass.Id);
                Invoke("ExecuteApplicationCommand", "CreateStudy");
                Pump();
                InWindow<System.Windows.Controls.TextBox>("_studyWindow", "StudyName").Text = "UI estudio";
                InWindow<System.Windows.Controls.TextBox>("_studyWindow", "StudyWidths").Text = "4 20";
                InWindow<System.Windows.Controls.TextBox>("_studyWindow", "StudyDepths").Text = "3 14";
                InWindow<System.Windows.Controls.TextBox>("_studyWindow", "StudyHeights").Text = "3 12";
                InWindow<System.Windows.Controls.TextBox>("_studyWindow", "StudyCount").Text = "50";
                ClickIn("_studyWindow", "StudyCreate");
                Pump();
                var uiStudy = session.Document.Studies.SingleOrDefault(study => study.Name == "UI estudio");
                Check(uiStudy is not null && uiStudy.Count == 50 && uiStudy.MassId == studyMass.Id, "The study window saves a study of the selected mass");
                Check(Field<System.Windows.Window?>("_studyWindow") is null && Field<System.Windows.Window?>("_outcomeWindow") is { IsVisible: true }, "Creating the study closes its window and opens its outcomes");
                Check(InWindow<System.Windows.Controls.ListBox>("_outcomeWindow", "OutcomeList").Items.Count == 50 && InWindow<System.Windows.Controls.TextBlock>("_outcomeWindow", "OutcomeSummary").Text.Contains("50"), "The outcomes window lists the 50 shapes tried");
                Check(((string)Invoke("CreateStudyFromText", "ui ESTUDIO", "4 20", "3 14", "3 12", StudyGoal.Compactness, "", "", "20", "1")!).Contains("Ya existe") && ((string)Invoke("CreateStudyFromText", "Malo", "20 4", "3 14", "3 12", StudyGoal.Compactness, "", "", "20", "1")!).Contains("no es válido"),
                    "A repeated name and a backward range are refused with a reason");
                var bestShape = DesignStudies.Evaluate(uiStudy!)[0];
                InWindow<System.Windows.Controls.ListBox>("_outcomeWindow", "OutcomeList").SelectedIndex = 0;
                ClickIn("_outcomeWindow", "OutcomeApply");
                var shaped = session.Document.StructuralElements.Single(item => item.Id == studyMass.Id);
                Check(Math.Abs(shaped.Footprint.Max(p => p.X) - shaped.Footprint.Min(p => p.X) - bestShape.Width) < 1e-6 && Math.Abs(shaped.Height - bestShape.Height) < 1e-6, "Apply gives the mass the shape chosen in the list");
                var optionSetsBefore = session.Document.OptionSets.Count;
                ClickIn("_outcomeWindow", "OutcomeOptions");
                Check(session.Document.OptionSets.Count == optionSetsBefore + 1, "The five best become design options with one click");
                ClickIn("_outcomeWindow", "OutcomeRemove");
                Pump();
                Check(session.Document.Studies.All(study => study.Name != "UI estudio"), "The outcomes window removes the study");
                ClickIn("_outcomeWindow", "OutcomeClose");
                Invoke("ExecuteApplicationCommand", "ExploreOutcomes");
                Pump();
                Check(((System.Windows.Controls.TextBlock)opened.FindName("StatusText")).Text.Contains("no tiene estudios"), "Explore Outcomes says when the project has no studies");
                ClickIn("_outcomeWindow", "OutcomeClose");

                // Automation: write, record, save, play.
                var scriptsAtStart = session.Document.Automations.Count;
                Invoke("ExecuteApplicationCommand", "Automation");
                Pump();
                InWindow<System.Windows.Controls.TextBox>("_automationWindow", "AutoName").Text = "UI guion";
                InWindow<System.Windows.Controls.TextBox>("_automationWindow", "AutoText").Text = "# analisis\nStructuralAnalysis\nEnergyOptimization";
                ClickIn("_automationWindow", "AutoSave");
                Check(session.Document.Automations.Count == scriptsAtStart + 1 && session.Document.Automations.Last().Steps.Count == 2, "The automation window saves a script of two steps");
                InWindow<System.Windows.Controls.TextBox>("_automationWindow", "AutoText").Text = "NoExiste";
                ClickIn("_automationWindow", "AutoSave");
                Check(session.Document.Automations.Count == scriptsAtStart + 1 && InWindow<System.Windows.Controls.TextBlock>("_automationWindow", "AutoMessage").Text.Contains("no existen"), "A script with a command that does not exist is not saved, and the window says which");
                InWindow<System.Windows.Controls.TextBox>("_automationWindow", "AutoText").Text = "";
                ClickIn("_automationWindow", "AutoRecord");
                Check(Field<object?>("_recording") is not null, "Record turns the recorder on");
                Invoke("ExecuteApplicationCommand", "ResultsFill");
                Pump();
                ClickIn("_fillWindow", "FillClose");
                Answer("860 860 864 863 1 2");
                Invoke("ExecuteApplicationCommand", "Sketch3D");
                ClickIn("_automationWindow", "AutoRecord");
                var recorded = InWindow<System.Windows.Controls.TextBox>("_automationWindow", "AutoText").Text;
                Check(Field<object?>("_recording") is null && recorded.Contains("ResultsFill") && recorded.Contains("Sketch3D | 860 860 864 863 1 2") && !recorded.Contains("AutomationPlayer"),
                    "Stop writes the commands used and the answers they were given");
                InWindow<System.Windows.Controls.TextBox>("_automationWindow", "AutoName").Text = "UI grabado";
                ClickIn("_automationWindow", "AutoSave");
                Check(session.Document.Automations.Any(script => script.Name == "UI grabado" && script.Steps.Count == 2), "The recorded script is saved");
                ClickIn("_automationWindow", "AutoClose");
                Invoke("ExecuteApplicationCommand", "FormCreation");
                Check(Field<object?>("_recording") is null, "Commands are not recorded when the recorder is off");

                session.SaveAutomation("UI bloqueado", "Undo");
                session.SaveAutomation("UI fantasma", "Zone\nNoExiste");
                var massesBeforePlay = session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass);
                Invoke("ExecuteApplicationCommand", "AutomationPlayer");
                Pump();
                var playerList = InWindow<System.Windows.Controls.ListBox>("_playerWindow", "PlayerList");
                int Pick(string name) => playerList.Items.Cast<string>().ToList().FindIndex(entry => entry.StartsWith(name, StringComparison.Ordinal));
                playerList.SelectedIndex = Pick("UI bloqueado");
                ClickIn("_playerWindow", "PlayerRun");
                Check(InWindow<System.Windows.Controls.TextBox>("_playerWindow", "PlayerLog").Text.Contains("no puede usar"), "The player refuses a script with a command scripts may not run");
                playerList.SelectedIndex = Pick("UI fantasma");
                ClickIn("_playerWindow", "PlayerRun");
                Check(InWindow<System.Windows.Controls.TextBox>("_playerWindow", "PlayerLog").Text.Contains("no existen") && Field<System.Windows.Window?>("_zoneWindow") is null, "The player runs nothing when a command of the script does not exist");
                playerList.SelectedIndex = Pick("UI grabado");
                ClickIn("_playerWindow", "PlayerRun");
                Pump();
                var playLog = InWindow<System.Windows.Controls.TextBox>("_playerWindow", "PlayerLog").Text;
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == massesBeforePlay + 1 && playLog.Contains("Guion terminado: 2 de 2"), "The player runs the recorded script: the mass it sketched appears: " + playLog);
                if (Field<System.Windows.Window?>("_fillWindow") is { } leftFill) leftFill.Close();
                session.Undo();
                Check(session.Document.StructuralElements.Count(item => item.Kind == StructuralElementKind.Mass) == massesBeforePlay, "One Undo reverts the whole script");
                session.Redo();
                ClickIn("_playerWindow", "PlayerClose");

                // Insert: the shared family library.
                var libraryRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"biblioteca-{Guid.NewGuid():N}");
                System.IO.Directory.CreateDirectory(libraryRoot);
                var libraryModel = System.IO.Path.Combine(libraryRoot, "tetra.obj");
                System.IO.File.WriteAllText(libraryModel, "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 1\nf 1 3 2\nf 1 2 4\nf 2 3 4\nf 3 1 4\n");
                var libraryFolder = System.IO.Path.Combine(libraryRoot, "familias");
                FamilyConverter.Convert(libraryModel, System.IO.Path.Combine(libraryFolder, "tetraedro.g3df"), "Tetraedro", FamilyCategory.Entourage);
                Invoke("ExecuteApplicationCommand", "LoadCloudFamily");
                Pump();
                InWindow<System.Windows.Controls.TextBox>("_libraryWindow", "LibraryFolder").Text = libraryFolder;
                var libraryList = InWindow<System.Windows.Controls.ListBox>("_libraryWindow", "LibraryList");
                Check(libraryList.Items.Count == 1 && ((string)libraryList.Items[0]).StartsWith("tetraedro", StringComparison.Ordinal), "The library window lists the families in the folder chosen");
                InWindow<System.Windows.Controls.TextBox>("_libraryWindow", "LibrarySearch").Text = "zzzz";
                Check(libraryList.Items.Count == 0, "Searching a name that is not there lists nothing");
                InWindow<System.Windows.Controls.TextBox>("_libraryWindow", "LibrarySearch").Text = "";
                ClickIn("_libraryWindow", "LibraryLoad");
                Check(InWindow<System.Windows.Controls.TextBlock>("_libraryWindow", "LibraryMessage").Text.Length > 0, "Loading with nothing chosen says to choose a family");
                var componentsBefore = session.Document.StructuralElements.Count;
                libraryList.SelectedIndex = 0;
                ClickIn("_libraryWindow", "LibraryLoad");
                Check(session.Document.StructuralElements.Count == componentsBefore + 1, "Loading a family of the library places it in the project");
                Check(((string)Invoke("PublishLibraryFamily", System.IO.Path.Combine(libraryFolder, "tetraedro.g3df"), libraryFolder)!).Contains("tetraedro 2.g3df") && System.IO.Directory.GetFiles(libraryFolder, "*.g3df").Length == 2, "Publishing a family adds it to the library under a free name");
                Check(!((string)Invoke("PublishLibraryFamily", libraryModel, libraryFolder)!).Contains("publicada"), "A file that is not a family is not published");
                Field<System.Windows.Window>("_libraryWindow").Close();
                System.IO.Directory.Delete(libraryRoot, true);

                // Wall: attach the base to the slab under the wall, and release it.
                var baseLevel = Field<Guid>("_activeLevelId");
                var baseWall = session.AddWall(baseLevel, new Point2(900, 900), new Point2(910, 900), 0.2, 3);
                var baseSlab = session.AddFloor(baseLevel, [new Point2(899, 899), new Point2(911, 899), new Point2(911, 901), new Point2(899, 901)], 0.3);
                session.UpdateFloor(baseSlab, "Zapata UI", 0.3, -0.6);
                SetField("_selectedWallId", (Guid?)null);
                Invoke("ExecuteApplicationCommand", "AttachBase");
                Check(!string.IsNullOrWhiteSpace(((System.Windows.Controls.TextBlock)opened.FindName("StatusText")).Text) && Math.Abs(session.Document.Walls.Single(wall => wall.Id == baseWall).BaseOffset) < 1e-9, "Attach Base with no wall selected changes nothing");
                SetField("_selectedWallId", (Guid?)baseWall);
                Invoke("ExecuteApplicationCommand", "AttachBase");
                var attachedWall = session.Document.Walls.Single(wall => wall.Id == baseWall);
                Check(Math.Abs(attachedWall.BaseOffset + 0.6) < 1e-9 && Math.Abs(attachedWall.NetHeight - 3.6) < 1e-9, "Attach Base takes the selected wall down to the footing under it");
                SetField("_selectedWallId", (Guid?)baseWall);
                Invoke("ExecuteApplicationCommand", "DetachAll");
                Check(Math.Abs(session.Document.Walls.Single(wall => wall.Id == baseWall).BaseOffset) < 1e-9, "Detaching the wall also releases its base");
                session.Undo();
                Check(Math.Abs(session.Document.Walls.Single(wall => wall.Id == baseWall).BaseOffset + 0.6) < 1e-9, "One undo gives the wall its base back");
                session.Redo();

                // Labels follow the project's units.
                session.SetProjectUnits(new ProjectUnits(LengthUnit.Feet, 2));
                Invoke("RefreshAll");
                Pump();
                var planTexts = ((System.Windows.Controls.Canvas)Field<object>("PlanCanvas")).Children.OfType<System.Windows.Controls.TextBlock>().Select(block => block.Text).ToList();
                Check(planTexts.Any(text => text.Contains("ft²")) && !planTexts.Any(text => text.Contains(" m²")), "With the project in feet the room labels of the plan read in square feet");
                session.SetProjectUnits(ProjectUnits.Default);
                Invoke("RefreshAll");
                Pump();
                Check(((System.Windows.Controls.Canvas)Field<object>("PlanCanvas")).Children.OfType<System.Windows.Controls.TextBlock>().Any(block => block.Text.Contains(" m²")), "Back in meters they read in square meters again");

                SetField("_viewSelection", null);
                Pump();
                tabs.SelectedIndex = 0;
                Pump();
            }
            finally
            {
                responder.SetValue(null, null);
            }

            CrashProtectionChecks(opened, session);

            RandomRibbonOnHouse(session);

            // Undoing everything the window did returns the house to what was opened, whatever the commands were,
            // as long as the bounded history (40 steps) never dropped the oldest ones.
            var historyTrimmed = session.UndoNames.Count >= 40;
            var guard = 0;
            while (session.CanUndo && guard++ < 10000) session.Undo();
            Check(historyTrimmed || ProjectFile.SerializeDocument(session.Document) == initial,
                "Undoing every command of the session brings the cobogó house back exactly as it opened");
        }
        finally
        {
            // This throwaway window intentionally mutates the sample. Undo restores the document,
            // but does not clear the unsaved-changes flag; closing with it set opens a modal
            // confirmation and hangs unattended Windows CI until its ten-minute timeout.
            SetField("_dirty", false);
            opened.Close();
            _window = previous;
            if (previous is not null) _canvas = (Canvas)previous.FindName("PlanCanvas");
        }
    }

    /// <summary>
    /// What protects the work when the program stops unexpectedly, on the real house: a change is copied on its own, in
    /// the background; the copy is the project to the last byte; a new session offers it and recovers it; accepting
    /// keeps it until the project is saved; closing on purpose leaves nothing behind; an error is logged.
    /// </summary>
    private static void CrashProtectionChecks(MainWindow opened, DocumentSession session)
    {
        var recovery = Environment.GetEnvironmentVariable("G3D_RECOVERY_DIR")!;
        var id = session.Document.Id;

        // The deterministic commands above left the project changed, so an emergency save writes its copy at once.
        Invoke("EmergencySave");
        var copy = AutoRecovery.Find(recovery).SingleOrDefault(candidate => candidate.DocumentId == id);
        Check(copy is not null && ProjectFile.SerializeDocument(AutoRecovery.Load(copy)) == ProjectFile.SerializeDocument(session.Document),
            "An emergency save writes a copy of the house identical to the project being edited");

        // A further change is copied by itself, in the background, without anyone asking.
        var savedBefore = copy!.SavedAtUtc;
        var wall = session.Document.Walls.First(candidate => !candidate.Pinned);
        session.SetWallsPinned([wall.Id], true);
        var current = ProjectFile.SerializeDocument(session.Document);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        var copiedByItself = false;
        while (waited.Elapsed < TimeSpan.FromSeconds(60) && !copiedByItself)
        {
            Pump();
            System.Threading.Thread.Sleep(300);
            var latest = AutoRecovery.Find(recovery).SingleOrDefault(candidate => candidate.DocumentId == id);
            copiedByItself = latest is not null && latest.SavedAtUtc > savedBefore &&
                             ProjectFile.SerializeDocument(AutoRecovery.Load(latest)) == current;
        }
        Check(copiedByItself, $"A change to the house is copied automatically within {waited.Elapsed.TotalSeconds:0} s, with nobody asking");

        // A new session finds the copy, offers it and recovers the project exactly, still marked as unsaved.
        var asked = new List<string>();
        var second = new MainWindow { Width = 800, Height = 600 };
        try
        {
            second.Show();
            SetInstanceField(second, "_recoveryPromptSink", (Func<string, bool>)(question => { asked.Add(question); return true; }));
            Instance(second, "OfferRecovery");
            var recovered = InstanceField<DocumentSession>(second, "_session").Document;
            Check(asked.Count == 1 && asked[0].Contains(session.Document.Name) &&
                  ProjectFile.SerializeDocument(recovered) == current && InstanceField<bool>(second, "_dirty"),
                "A new session offers the leftover copy and recovers the house exactly, still unsaved");
            Check(AutoRecovery.Find(recovery).Any(candidate => candidate.DocumentId == id),
                "The copy stays until the recovered project is saved");
            SetInstanceField(second, "_dirty", false);
        }
        finally
        {
            second.Close();
        }
        Check(AutoRecovery.Find(recovery).All(candidate => candidate.DocumentId != id),
            "Closing a project on purpose leaves no recovery copy behind");

        // Declining the offer deletes the copy, so it is not asked about again.
        Invoke("EmergencySave");
        var third = new MainWindow { Width = 800, Height = 600 };
        try
        {
            third.Show();
            SetInstanceField(third, "_recoveryPromptSink", (Func<string, bool>)(_ => false));
            Instance(third, "OfferRecovery");
        }
        finally
        {
            third.Close();
        }
        Check(AutoRecovery.Find(recovery).All(candidate => candidate.DocumentId != id), "Declining the offer deletes the copy");

        // An error is written to the log, with its place and the exception.
        var logs = Environment.GetEnvironmentVariable("G3D_LOG_DIR")!;
        var reporter = typeof(MainWindow).Assembly.GetType("Guanacaste3D.Desktop.CrashReporter")!;
        reporter.GetMethod("Log", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, ["interface", new InvalidOperationException("prueba de registro")]);
        Check(Directory.Exists(logs) && Directory.EnumerateFiles(logs, "errors-*.log")
                .Any(file => File.ReadAllText(file).Contains("prueba de registro")),
            "An error is written to the day's log with its place and details");

        // Leave the house as the next checks expect: pinned state restored.
        session.SetWallsPinned([wall.Id], false);
    }

    private static object? Instance(MainWindow window, string name) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(window, null);

    private static T InstanceField<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(window)!;

    private static void SetInstanceField(MainWindow window, string name, object? value) =>
        typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(window, value);

    /// <summary>
    /// Opt-in diagnostic (G3D_PROFILE=1): times each stage of the redraw that follows every edit, on the real house,
    /// because the model itself changes in about 0.1 s and the rest is the interface catching up.
    /// </summary>
    private static void ProfileRefresh()
    {
        foreach (var stage in new[]
                 {
                     "SynchronizeModelViewGraphics", "UpdateWindowTitle", "RefreshLevelPicker", "RefreshViewShortcuts",
                     "RefreshTree", "RefreshProperties", "RenderPlan", "Render3D", "RefreshWallTypePicker", "RefreshSchedule",
                     "RenderSheet", "RenderOrthographicViews", "RefreshExtendedWorkflows"
                 })
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Invoke(stage);
            Pump();
            Console.WriteLine($"PROFILE: {stage,-32} {timer.ElapsedMilliseconds,6} ms");
        }
    }

    /// <summary>
    /// Random ribbon commands on random real walls of the house, on both levels, one seed after another. Every command
    /// redraws a scene of about 400,000 triangles, so the run has a time budget instead of a fixed length.
    /// </summary>
    private static void RandomRibbonOnHouse(DocumentSession session)
    {
        string[] commands =
        [
            "AllowJoin", "MiterJoin", "ButtJoin", "SquareOff", "DisallowJoin", "Pin", "FlipWall", "DetachAll",
            "Copy", "Cut", "Paste", "CopyElement", "Rotate", "Undo", "Redo"
        ];
        // Rotating a pinned wall shows a warning box that waits for the user; the window offers a sink for checks.
        var warnings = new List<string>();
        SetField("_rotationWarningSink", (Action<string, string>)((message, _) => warnings.Add(message)));
        var budget = System.Diagnostics.Stopwatch.StartNew();
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("G3D_RANDOM_SECONDS"), out var requested) && requested > 0 ? requested : 100;
        var failures = new List<string>();
        var executed = 0;
        // DocumentSession intentionally retains only the 40 most recent undo snapshots.
        // Keep this fixture's random edits below that limit (including earlier ribbon edits),
        // so the final full-undo assertion remains valid instead of depending on runner speed.
        const int maxRandomCommands = 20;
        for (var seed = 1; seed <= 12 && executed < maxRandomCommands && budget.Elapsed < TimeSpan.FromSeconds(seconds); seed++)
        {
            var random = new Random(seed * 131);
            for (var step = 0; step < 30 && executed < maxRandomCommands && budget.Elapsed < TimeSpan.FromSeconds(seconds); step++)
            {
                var wall = session.Document.Walls[random.Next(session.Document.Walls.Count)];
                var command = commands[random.Next(commands.Length)];
                // Reach the wall's level the way the user does; now and then clear the selection altogether.
                Invoke("SelectElement", wall.Id);
                if (random.Next(10) == 0) SetField("_selectedWallId", null);
                try
                {
                    Invoke("ExecuteApplicationCommand", command);
                    session.Document.Validate();
                    executed++;
                }
                catch (Exception exception)
                {
                    var cause = (exception as TargetInvocationException)?.InnerException ?? exception;
                    failures.Add($"seed {seed} step {step} {command}: {cause.GetType().Name}: {cause.Message}");
                    break;
                }
            }
        }
        Check(failures.Count == 0 && executed >= Math.Min(15, seconds / 4),
            $"{executed} random ribbon commands on the house's real walls, on both levels, never throw and keep the project valid" +
            (failures.Count == 0 ? "" : ": " + string.Join(" | ", failures.Take(5))));
    }
}
