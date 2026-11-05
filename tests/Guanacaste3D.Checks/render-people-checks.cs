using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the people of the render: their routes over time, their saved text, their bodies, and that a tracer moves them.</summary>
static class RenderPeopleChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Routes over time.
        Actor Walker(bool loop, double start, params ActorPoint[] route) => new(Guid.NewGuid(), "Ana", 2, 2, loop, start, route);
        var straight = Walker(false, 1, new ActorPoint(0, 0, 0), new ActorPoint(10, 0, 0));
        var (before, early, middle, finished) = (Actors.At(straight, 0), Actors.At(straight, 1), Actors.At(straight, 3), Actors.At(straight, 9));
        check(!before.Walking && before.Position.X == 0 && !early.Walking && middle.Walking && Math.Abs(middle.Position.X - 4) < 1e-4 &&
              Math.Abs(middle.Heading) < 1e-4 && !finished.Walking && Math.Abs(finished.Position.X - 10) < 1e-4,
            "A person stands before it sets off, walks the route at its speed, and stops at the last point");
        var back = Walker(true, 1, new ActorPoint(0, 0, 0), new ActorPoint(10, 0, 0));
        var (atEnd, returning) = (Actors.At(back, 6), Actors.At(back, 8));
        check(Math.Abs(atEnd.Position.X - 10) < 1e-4 && Math.Abs(returning.Position.X - 6) < 1e-4 && Math.Abs(Math.Abs(returning.Heading) - Math.PI) < 1e-4 && returning.Walking,
            "A looping person turns round at the end and walks back, facing the way it goes");
        var corner = Walker(false, 0, new ActorPoint(0, 0, 0), new ActorPoint(4, 0, 0), new ActorPoint(4, 4, 1));
        var turned = Actors.At(corner, 3);
        check(Math.Abs(turned.Position.X - 4) < 1e-4 && Math.Abs(turned.Position.Y - 2) < 1e-4 && Math.Abs(turned.Heading - Math.PI / 2) < 1e-4 &&
              Math.Abs(turned.Position.Z - 0.5) < 1e-4,
            "A route with a corner turns the person, and the height follows the route");
        check(!Actors.At(Walker(false, 0, new ActorPoint(3, 4, 0)), 5).Walking && !Actors.At(Walker(false, 0, new ActorPoint(1, 1, 0), new ActorPoint(1, 1, 0)), 5).Walking,
            "A person with one point, or a route of no length, stands still");

        // Saved text.
        var actors = new List<Actor> { straight, back with { Look = 5, Name = "Luis" } };
        check(Actors.TryParse(Actors.Serialize(actors)) is { Count: 2 } read && read[0].Route.SequenceEqual(straight.Route) && read[1].Look == 5 && read[1].Loop &&
              Actors.Serialize([]) is null && Actors.TryParse(null)!.Count == 0,
            "People read back exactly from the text the project keeps, and none keep nothing");
        string[] bad = ["no es json", "{}", """{"V":2,"Items":[]}""",
            """{"V":1,"Items":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"x","Look":9,"Speed":1,"Loop":false,"Start":0,"Route":[[0,0,0]]}]}""",
            """{"V":1,"Items":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"x","Look":1,"Speed":0,"Loop":false,"Start":0,"Route":[[0,0,0]]}]}""",
            """{"V":1,"Items":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"x","Look":1,"Speed":1,"Loop":false,"Start":0,"Route":[]}]}""",
            """{"V":1,"Items":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"x","Look":1,"Speed":1,"Loop":false,"Start":0,"Route":[[0,0]]}]}"""];
        check(bad.All(item => Actors.TryParse(item) is null), "Damaged people text, an impossible look or speed, and a route with no points or a bad point are refused");

        // The body.
        var rig = ActorRig.From([straight])!;
        var standing = rig.Positions(0);
        check(rig.Indices.Length % 3 == 0 && rig.Indices.Max() < rig.VertexCount && rig.TriangleMaterial.Length == rig.Indices.Length / 3 &&
              rig.TriangleMaterial.Max() < rig.Materials.Length && rig.Indices.Length / 3 is > 200 and < 800 && standing.All(point => float.IsFinite(point.X + point.Y + point.Z)),
            $"A person is a few hundred consistent triangles ({rig.Indices.Length / 3}) with its five materials");
        var height = standing.Max(point => point.Z) - standing.Min(point => point.Z);
        check(Math.Abs(height - 1.815f * (0.90f + 0.018f * 2)) < 0.05f && Math.Abs(standing.Min(point => point.Z)) < 0.01f,
            $"A standing person is about as tall as a person and stands on the ground ({height:0.00} m)");
        var walking = rig.Positions(3);
        var swing = Enumerable.Range(0, standing.Length).Max(index => Vector3.Distance(walking[index] - Actors.At(straight, 3).Position, standing[index] - Actors.At(straight, 0).Position));
        check(swing > 0.05f, $"A walking person swings its legs and arms ({swing:0.00} m at most)");
        (float X, float Y) Extent(Vector3[] points) => (points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
        var facingX = Extent(ActorRig.From([Walker(false, 0, new ActorPoint(0, 0, 0), new ActorPoint(5, 0, 0))])!.Positions(0));
        var facingY = Extent(ActorRig.From([Walker(false, 0, new ActorPoint(0, 0, 0), new ActorPoint(0, 5, 0))])!.Positions(0));
        check(facingX.Y > facingX.X && facingY.X > facingY.Y, "A person walking along X is wide across Y, and walking along Y wide across X: it faces where it goes");
        check(Enumerable.Range(0, Actor.Looks).Select(look => ActorRig.From([straight with { Look = look }])!.Materials[1].BaseColor).Distinct().Count() == Actor.Looks,
            "Each of the six looks has its own clothes");

        // A tracer moves them without moving the scene it was given.
        var ground = new SceneMesh();
        ground.AddQuad(new(-100, -100, 0), new(100, -100, 0), new(100, 100, 0), new(-100, 100, 0), SurfaceKind.Ground);
        var standingAtOrigin = Walker(false, 0, new ActorPoint(0, 0, 0));
        var one = ActorRig.From([standingAtOrigin])!;
        var withPerson = new RenderSceneBuilder().AddBuilding(ground).AddDynamic(one.Positions(0), one.Indices, one.TriangleMaterial, one.Materials).Build();
        var empty = new RenderSceneBuilder().AddBuilding(ground).Build();
        var camera = new RenderCamera(new Vector3(0, -6, 1.5f), new Vector3(0, 0, 0.9f), 45);
        var settings = new RenderSettings { Seed = 3 };
        byte[] Shot(RenderScene scene, Action<PathTracer>? change = null)
        {
            var tracer = new PathTracer(scene, 96, 64, camera, settings);
            change?.Invoke(tracer);
            for (var pass = 0; pass < 3; pass++) tracer.RenderPass();
            return tracer.ToBgra32(finish: ImageProcessor.Plain);
        }
        // Pixels where the two pictures differ clearly: a person is a small figure, so a mean over the picture would hide it.
        double Difference(byte[] a, byte[] b) => Enumerable.Range(0, a.Length / 4).Count(pixel =>
            Enumerable.Range(0, 3).Any(channel => Math.Abs(a[pixel * 4 + channel] - b[pixel * 4 + channel]) > 25));
        var bare = Shot(empty);
        var there = Shot(withPerson);
        var gone = Shot(withPerson, tracer => tracer.MoveActors(one.Positions(0).Select(point => point + new Vector3(300, 0, 0)).ToArray()));
        check(withPerson.DynamicTriangleCount == one.Indices.Length / 3 && withPerson.DynamicVertexCount == one.VertexCount && empty.DynamicTriangleCount == 0 &&
              Difference(bare, there) > 40 && Difference(bare, gone) < 6,
            $"A person in the scene shows in the picture, and moved out of view leaves it as it was ({Difference(bare, there)} pixels differ with, {Difference(bare, gone)} moved away)");
        check(withPerson.Positions[withPerson.DynamicVertexStart] == one.Positions(0)[0],
            "Moving a person in one tracer leaves the shared scene as it was");
        var failures = 0;
        try { new PathTracer(empty, 8, 8, camera).MoveActors(new Vector3[3]); } catch (InvalidOperationException) { failures++; }
        try { new PathTracer(withPerson, 8, 8, camera).MoveActors(new Vector3[3]); } catch (ArgumentException) { failures++; }
        try { new RenderSceneBuilder().AddDynamic(one.Positions(0), one.Indices, one.TriangleMaterial, one.Materials).AddBuilding(ground); } catch (InvalidOperationException) { failures++; }
        check(failures == 3, "Moving people needs a scene that has them, the right number of vertices, and they must be added last");

        // The movie moves them frame by frame.
        var path = new CameraPath();
        path.Add(new CameraMark(camera.Position, camera.Target, 45, 0));
        path.Add(new CameraMark(camera.Position, camera.Target, 45, 2));
        var crossing = ActorRig.From([Walker(false, 0, new ActorPoint(-1.5, 0, 0), new ActorPoint(1.5, 0, 0)) with { Speed = 1.5 }])!;
        var crossingScene = new RenderSceneBuilder().AddBuilding(ground).AddDynamic(crossing.Positions(0), crossing.Indices, crossing.TriangleMaterial, crossing.Materials).Build();
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-people-" + Guid.NewGuid().ToString("N"));
        try
        {
            TourRenderer.RenderAsync(crossingScene, path, settings, 96, 64, 4, 3, new ImageFinish(), Path.Combine(folder, "walk"), null,
                CancellationToken.None, null, crossing).GetAwaiter().GetResult();
            TourRenderer.RenderAsync(crossingScene, path, settings, 96, 64, 4, 3, new ImageFinish(), Path.Combine(folder, "still"), null,
                CancellationToken.None).GetAwaiter().GetResult();
            var walk = Directory.GetFiles(Path.Combine(folder, "walk"), "frame_*.png").Order().ToArray();
            var still = Directory.GetFiles(Path.Combine(folder, "still"), "frame_*.png").Order().ToArray();
            double Frames(string a, string b)
            {
                var (x, y) = (PngCodec.Load(a).Rgb, PngCodec.Load(b).Rgb);
                return Enumerable.Range(0, x.Length / 3).Count(pixel => Enumerable.Range(0, 3).Any(channel => Math.Abs(x[pixel * 3 + channel] - y[pixel * 3 + channel]) > 25));
            }
            check(walk.Length == still.Length && walk.Length >= 8 && Frames(walk[0], walk[^1]) > Frames(still[0], still[^1]) + 40,
                $"In the video the camera is still and the person crosses the view ({Frames(walk[0], walk[^1])} pixels change against {Frames(still[0], still[^1])})");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }

        // Cars: the shipped sedan and coupé drive a route, turn smoothly at corners and keep their smooth normals turned with them.
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Guanacaste3D.slnx"))) { root = directory.FullName; break; }
        if (root is not null)
        {
            var carFiles = new[] { "sedan-car", "coupe-car-silver" };
            var meshes = new Dictionary<int, FamilyMesh.Decoded?>();
            FamilyMesh.Decoded? Model(int model) => meshes.TryGetValue(model, out var decoded) ? decoded : meshes[model] =
                NativeDocumentStore.LoadFamily(Path.Combine(root, "src", "Guanacaste3D.Desktop", "resources", "families", carFiles[model] + ".g3df")).Mesh!.Decode();
            Actor Car(int look, params ActorPoint[] route) => new(Guid.NewGuid(), "Coche", look, 8, false, 0, route, ActorKind.Car);
            var drive = Car(2, new ActorPoint(0, 0, 0), new ActorPoint(40, 0, 0));
            check(ActorRig.From([drive]) is null && ActorRig.From([drive, standingAtOrigin]) is { Count: 1 },
                "Cars need their meshes: without them a car is left out, and the people are still drawn");
            var carRig = ActorRig.From([drive, Car(3, new ActorPoint(0, 10, 0), new ActorPoint(0, 30, 0))], Model)!;
            check(carRig.Count == 2 && carRig.Indices.Length / 3 > 40000 && carRig.HasNormals && carRig.Materials.Length >= 6 &&
                  carRig.TriangleMaterial.Max() < carRig.Materials.Length,
                $"A car is a few thousand triangles of its own with paint, rubber, metal and glass ({carRig.Indices.Length / 3} for two)");
            var (carPositions, carNormals) = carRig.Frame(2);
            var firstCar = carPositions.Take(carPositions.Length / 2 - 0).ToArray();
            check(carNormals.Take(1000).All(normal => normal == Vector3.Zero || Math.Abs(normal.Length() - 1) < 1e-3f) && carNormals.Any(normal => normal != Vector3.Zero),
                "The cars' smooth normals stay unit length when the car is turned");
            var carRed = ActorRig.From([Car(2, new ActorPoint(0, 0, 0))], Model)!.Materials;
            var carSilver = ActorRig.From([Car(3, new ActorPoint(0, 0, 0))], Model)!.Materials;
            check(carRed.Any(material => material.BaseColor == new Vector3(0.42f, 0.025f, 0.025f)) &&
                  carSilver.Any(material => material.BaseColor == MaterialLibrary.ForKind(SurfaceKind.CarPaintSilver).BaseColor),
                "A look repaints the car, and the silver coupé keeps its own paint");
            // Wheels: four are found on each model, they turn with the distance covered and the body does not.
            foreach (var model in new[] { 0, 1 })
            {
                var template = new CarTemplate(Model(model)!);
                var spinning = Enumerable.Range(0, template.Positions.Length).Where(vertex => template.WheelOf(vertex) >= 0).ToArray();
                check(template.Wheels.Count == 4 && template.Wheels.All(wheel => wheel.Radius is > 0.28f and < 0.40f) &&
                      Enumerable.Range(0, 4).All(wheel => spinning.Count(vertex => template.WheelOf(vertex) == wheel) > 1500),
                    $"The {(model == 0 ? "sedan" : "coupé")} has four wheels of about {template.Wheels[0].Radius:0.00} m radius, each with its tyre, rim and hub ({spinning.Length} vertices)");
                var (standingPose, turnedPose, oneTurnPose) = (new Vector3[template.Positions.Length], new Vector3[template.Positions.Length], new Vector3[template.Positions.Length]);
                var (spare, turnedNormals) = (new Vector3[template.Positions.Length], new Vector3[template.Positions.Length]);
                var origin = new ActorState(Vector3.Zero, 0, 0, true);
                template.Pose(origin, standingPose, spare);
                template.Pose(origin with { Distance = 3.3f }, turnedPose, turnedNormals);
                template.Pose(origin with { Distance = 2 * MathF.PI * template.Wheels[0].Radius }, oneTurnPose, spare);
                var body = Enumerable.Range(0, standingPose.Length).Where(vertex => template.WheelOf(vertex) < 0).ToArray();
                var wheelMoved = spinning.Count(vertex => Vector3.Distance(standingPose[vertex], turnedPose[vertex]) > 0.02f);
                var wheelBack = spinning.Where(vertex => template.WheelOf(vertex) == 0).All(vertex => Vector3.Distance(standingPose[vertex], oneTurnPose[vertex]) < 2e-3f);
                check(body.All(vertex => standingPose[vertex] == turnedPose[vertex]) && wheelMoved > spinning.Length / 2 && wheelBack,
                    $"The wheels turn with the distance and are back where they were after one turn, and the body does not move ({wheelMoved} wheel vertices moved)");
                var top = spinning.Where(vertex => template.WheelOf(vertex) == 0).OrderByDescending(vertex => template.Positions[vertex].Z).First();
                var small = new Vector3[template.Positions.Length];
                template.Pose(origin with { Distance = 0.05f }, small, spare);
                check(small[top].X > standingPose[top].X, "The top of a wheel goes the way the car goes, so it rolls forward and not backward");
                // Steering and lean: the two wheels at the nose turn into a curve, the others only ride with the body, which leans out.
                var steering = Enumerable.Range(0, 4).Where(template.Steers).ToList();
                var (flat, curved) = (new Vector3[template.Positions.Length], new Vector3[template.Positions.Length]);
                var heading = -MathF.PI / 2;
                template.Pose(new ActorState(Vector3.Zero, heading, 0, true, 0, 8), flat, spare);
                template.Pose(new ActorState(Vector3.Zero, heading, 0, true, 0.25f, 8), curved, spare);
                float Shift(int wheel) => spinning.Where(vertex => template.WheelOf(vertex) == wheel).Average(vertex => Vector3.Distance(flat[vertex], curved[vertex]));
                var roof = Enumerable.Range(0, flat.Length).OrderByDescending(vertex => flat[vertex].Z).First();
                check(steering.Count == 2 && steering.All(wheel => template.Wheels[wheel].Y * CarTemplate.Forward > 0) &&
                      Math.Abs(template.SteerAngle(0.25f) - MathF.Atan((template.Wheels.Max(wheel => wheel.Y) - template.Wheels.Min(wheel => wheel.Y)) * 0.25f)) < 1e-5f &&
                      template.SteerAngle(50) == CarTemplate.MaximumSteer && template.SteerAngle(-50) == -CarTemplate.MaximumSteer && template.SteerAngle(0) == 0,
                    "Only the two wheels at the nose steer, by the angle a car of that wheelbase needs for the curve, never past the steering lock");
                check(steering.All(wheel => Shift(wheel) > 0.05f) && Enumerable.Range(0, 4).Except(steering).All(wheel => Shift(wheel) < 0.06f) &&
                      curved[roof].X < flat[roof].X - 0.02f && curved[roof].Z > flat[roof].Z - 0.02f,
                    "In a left curve the front wheels turn left while the rear ones stay put, and the roof leans out to the right");
                template.Pose(new ActorState(Vector3.Zero, heading, 0, true, 0, 8), curved, spare);
                check(curved.SequenceEqual(flat) && CarTemplate.Lean(0.25f, 0) == 0 && CarTemplate.Lean(-0.25f, 8) > 0 && Math.Abs(CarTemplate.Lean(5, 40)) <= 0.061f,
                    "On a straight, or standing, the car does not steer or lean, and the lean is limited");
            }
            var leftCorner = Car(0, new ActorPoint(0, 0, 0), new ActorPoint(20, 0, 0), new ActorPoint(20, 20, 0)) with { Speed = 5 };
            var turning = Actors.At(leftCorner, 4).Turn;
            var righting = Actors.At(leftCorner with { Route = [new ActorPoint(0, 0, 0), new ActorPoint(20, 0, 0), new ActorPoint(20, -20, 0)] }, 4).Turn;
            check(Actors.At(leftCorner, 1).Turn == 0 && turning > 0.15f && righting < -0.15f && Actors.At(leftCorner, 4).Speed == 5 && Actors.At(leftCorner, 100).Turn == 0,
                $"A car on a straight does not turn, one rounding a left corner turns left ({turning:0.00} rad/m) and a right corner right, and one parked has stopped");
            // Mass scatter: a crowd and a line of traffic spread over the ground, each its own, reproducible, and only where there is ground.
            var squareCentre = new ActorPoint(10, 20, 0);
            var crowd = ActorScatter.Scatter(squareCentre, 8, 30, ActorKind.Person, true, 7, (x, y) => 0.0);
            double Gap(IReadOnlyList<Actor> group) => group.SelectMany((a, i) => group.Skip(i + 1).Select(b => Math.Sqrt(Math.Pow(a.Route[0].X - b.Route[0].X, 2) + Math.Pow(a.Route[0].Y - b.Route[0].Y, 2)))).Min();
            check(crowd.Count == 30 && crowd.All(person => person.IsValid && Math.Sqrt(Math.Pow(person.Route[0].X - 10, 2) + Math.Pow(person.Route[0].Y - 20, 2)) <= 8.001) &&
                  Gap(crowd) >= 0.8 && crowd.Select(person => person.Look).Distinct().Count() >= 4 && crowd.Select(person => person.Speed).Distinct().Count() > 20 && crowd.Any(person => person.Route.Count > 2) &&
                  crowd.Select(person => person.Id).Distinct().Count() == 30 && crowd.Last().Name == "Persona 30",
                $"A crowd of {crowd.Count} people is spread inside the circle, at least 0.8 m apart, each with its own look, speed and route");
            var again = ActorScatter.Scatter(squareCentre, 8, 30, ActorKind.Person, true, 7, (x, y) => 0.0);
            check(again.Select(person => (person.Route[0], person.Look, person.Speed)).SequenceEqual(crowd.Select(person => (person.Route[0], person.Look, person.Speed))) &&
                  !ActorScatter.Scatter(squareCentre, 8, 30, ActorKind.Person, true, 8, (x, y) => 0.0).Select(person => person.Route[0]).SequenceEqual(crowd.Select(person => person.Route[0])),
                "The same seed gives the same crowd and another seed another one");
            var traffic = ActorScatter.Scatter(squareCentre, 40, 10, ActorKind.Car, true, 3, (x, y) => 0.0, 5);
            var parked = ActorScatter.Scatter(squareCentre, 8, 5, ActorKind.Person, false, 1, (x, y) => 0.0);
            check(traffic.Count == 10 && Gap(traffic) >= 4 && traffic.All(vehicle => vehicle.Kind == ActorKind.Car && vehicle.Speed >= 6 && vehicle.IsValid && vehicle.Route.Count > 2) &&
                  traffic[0].Name == "Coche 5" && parked.All(person => person.Route.Count == 1),
                "Cars keep four metres apart and drive routes of their own, and people who are not asked to walk stand still");
            var onlyEast = ActorScatter.Scatter(squareCentre, 8, 30, ActorKind.Person, true, 7, (x, y) => x > 10 ? 0.0 : null);
            var ledge = ActorScatter.Scatter(squareCentre, 8, 30, ActorKind.Person, true, 7, (x, y) => x > 12 ? 5.0 : 0.0);
            check(onlyEast.Count > 0 && onlyEast.All(person => person.Route.All(point => point.X > 10)) && ActorScatter.Scatter(squareCentre, 8, 30, ActorKind.Person, true, 7, (x, y) => null).Count == 0 &&
                  ledge.All(person => person.Route.All(point => Math.Abs(point.Z - person.Route[0].Z) < 1e-9)) &&
                  ActorScatter.Scatter(squareCentre, 0.5, 5, ActorKind.Person, true, 1, (x, y) => 0.0).Count == 0 && ActorScatter.Scatter(squareCentre, 8, 0, ActorKind.Person, true, 1, (x, y) => 0.0).Count == 0 &&
                  ActorScatter.Scatter(squareCentre, 500, 500, ActorKind.Person, true, 1, (x, y) => 0.0).Count <= ActorScatter.MaximumPerBatch,
                "Nobody is put where there is no ground, a route stops at a step, and bad sizes or counts give nobody and the batch has a limit");
            var runOnce = Car(0, new ActorPoint(0, 0, 0), new ActorPoint(10, 0, 0)) with { Speed = 5 };
            check(Math.Abs(Actors.At(runOnce, 3).Distance - 10) < 1e-3f && Actors.At(runOnce, 30).Distance == Actors.At(runOnce, 3).Distance && !Actors.At(runOnce, 30).Walking &&
                  Actors.At(runOnce with { Loop = true }, 30).Distance > 100,
                "A car parked at the end of its route stops turning its wheels, and one that goes back and forth keeps driving forward");
            (float X, float Y) Span(Actor actor, double seconds)
            {
                var points = ActorRig.From([actor], Model)!.Positions(seconds);
                return (points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
            }
            var alongX = Span(Car(0, new ActorPoint(0, 0, 0), new ActorPoint(30, 0, 0)), 1);
            var alongY = Span(Car(0, new ActorPoint(0, 0, 0), new ActorPoint(0, 30, 0)), 1);
            check(alongX.X > 4 && alongX.Y < 2.3 && alongY.Y > 4 && alongY.X < 2.3, $"A car is about 4.7 m long and 1.9 m wide and drives lengthways along its route ({alongX.X:0.0} x {alongX.Y:0.0})");

            // A corner: the car's heading turns over the stretch round it and never jumps.
            var cornering = Walker(false, 0, new ActorPoint(0, 0, 0), new ActorPoint(10, 0, 0), new ActorPoint(10, 10, 0));
            var headings = Enumerable.Range(0, 101).Select(step => Actors.At(cornering, step * 0.1).Heading).ToArray();
            var biggestStep = Enumerable.Range(1, 100).Max(index => Math.Abs(headings[index] - headings[index - 1]));
            check(biggestStep < 0.35 && Math.Abs(Actors.At(cornering, 5).Heading - Math.PI / 4) < 0.1 && Math.Abs(headings[0]) < 1e-4 && Math.Abs(headings[^1] - Math.PI / 2) < 1e-4,
                $"Round a corner the heading turns over a short stretch (biggest step {biggestStep:0.00} rad), not all at once");

            // The tracer moves a car and its normals together: the picture changes and stays valid.
            var carScene = new RenderSceneBuilder().AddBuilding(ground);
            var (startPositions, startNormals) = carRig.Frame(0);
            var withCars = carScene.AddDynamic(startPositions, carRig.Indices, carRig.TriangleMaterial, carRig.Materials, startNormals).Build();
            var carCamera = new RenderCamera(new Vector3(20, -22, 4), new Vector3(20, 0, 0.8f), 55);
            byte[] CarShot(double seconds)
            {
                var tracer = new PathTracer(withCars, 96, 64, carCamera, settings);
                var (positions, normals) = carRig.Frame(seconds);
                tracer.MoveActors(positions, normals);
                for (var pass = 0; pass < 3; pass++) tracer.RenderPass();
                return tracer.ToBgra32(finish: ImageProcessor.Plain);
            }
            var firstShot = CarShot(0.5);
            var secondShot = CarShot(4);
            check(Difference(firstShot, secondShot) > 100 && withCars.DynamicTriangleCount == carRig.Indices.Length / 3,
                $"A car drives across the picture: {Difference(firstShot, secondShot)} pixels differ between two instants");
        }

        // Projects saved before cars existed hold people only.
        check(Actors.TryParse("""{"V":1,"Items":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"Vieja","Look":1,"Speed":1.2,"Loop":true,"Start":0,"Route":[[0,0,0]]}]}""") is { Count: 1 } old &&
              old[0].Kind == ActorKind.Person && Actors.TryParse(Actors.Serialize([standingAtOrigin with { Kind = ActorKind.Car }]))![0].Kind == ActorKind.Car,
            "A project saved before cars existed reads as people, and a car keeps its kind");

        // The cobogó house keeps working with people in it, and reads none from a project without them.
        var house = BreezeBlockHouseShowcase.Create();
        check(ActorRig.Of(house) is null, "A project without people has no rig");
        house.Extensions[Actors.Key] = Actors.Serialize([standingAtOrigin])!;
        check(ActorRig.Of(house) is { VertexCount: > 100 } && Actors.Of(house).Count == 1, "A project's people are read from its extension store");
    }
}
