using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Import.Revit;
using Guanacaste3D.Desktop;

internal static partial class InteractionChecks
{
    /// <summary>
    /// Opens a Revit exchange file the way a user does: the conversion window appears, "Convertir y abrir" converts it
    /// and reports what became native, "Abrir proyecto" opens it on the ground floor plan. G3D_REVIT_SAMPLE may name a
    /// real .g3dx export to run the same steps on.
    /// </summary>
    private static void RevitProjectChecks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-revit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Environment.GetEnvironmentVariable("G3D_REVIT_SAMPLE") is { } sample && File.Exists(sample)
                ? sample
                : WriteSampleExchange(Path.Combine(folder, "Casa de prueba.g3dx"));
            SetField("_dirty", false);
            var stage = 0;
            var driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            driver.Tick += (_, _) =>
            {
                foreach (var window in Application.Current.Windows.OfType<RevitProjectWindow>().Where(window => window.IsVisible))
                {
                    var start = typeof(RevitProjectWindow).GetMethod("Start_Click", PrivateInstance)!;
                    if (stage == 0)
                    {
                        // Families are not written next to a sample the user owns.
                        ((CheckBox)typeof(RevitProjectWindow).GetField("_saveFamilies", PrivateInstance)!.GetValue(window)!).IsChecked = false;
                        stage = 1;
                        start.Invoke(window, [window, new RoutedEventArgs()]);
                    }
                    else if (stage == 1 && window.Result is not null)
                    {
                        stage = 2;
                        SaveWindow(window, "artifacts/interaction-evidence/revit-conversion.png");
                        start.Invoke(window, [window, new RoutedEventArgs()]);
                    }
                }
            };
            driver.Start();
            var opened = (bool)Invoke("OpenRevitProject", path)!;
            driver.Stop();
            Check(opened && stage == 2, "A Revit exchange file converts in its window and opens as a project");
            var document = Field<DocumentSession>("_session").Document;
            Check(document.Walls.Count > 0 && document.Openings.Count > 0, "The opened project has native walls with doors and windows");
            var ground = document.Levels.Single(level => level.Role == LevelRole.BaseFloor);
            Check(Field<Guid>("_activeLevelId") == ground.Id, "The project opens on the ground floor plan");
            Check(Field<bool>("_dirty"), "A converted project is marked as not yet saved");
            Pump();
            SaveBitmap("artifacts/interaction-evidence/revit-project-plan.png");
            var tabs = (TabControl)_window.FindName("ViewTabs");
            tabs.SelectedIndex = 1;
            WaitFor(() => false, 4);
            SaveBitmap("artifacts/interaction-evidence/revit-project-3d.png");
            SetField("_dirty", false);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { /* temporary */ }
        }
    }

    private static void SaveWindow(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>A two-room house: four walls, a door, a window, a floor and a sofa.</summary>
    private static string WriteSampleExchange(string path)
    {
        var file = new ExchangeFile
        {
            Format = "guanacaste-exchange", Version = 1, ProjectName = "Casa de prueba",
            Levels = [new() { Id = 1, Name = "Nivel 1", Elevation = 0 }, new() { Id = 2, Name = "Nivel 2", Elevation = 3 }],
            Materials = [new() { Name = "Repello" }, new() { Name = "Tela" }],
            Meshes = [Box(new Vector3(-0.9f, -0.4f, 0), new Vector3(0.9f, 0.4f, 0.8f), 1)]
        };
        double[][] corners = [[0, 0], [8, 0], [8, 5], [0, 5]];
        for (var index = 0; index < 4; index++)
            file.Elements.Add(new ExchangeElement
            {
                Id = 10 + index, Category = "OST_Walls", Kind = "wall", Type = "Muro 15", Level = 1,
                Wall = new ExchangeWall
                {
                    Width = 0.15, Function = "Exterior", Start = corners[index], End = corners[(index + 1) % 4], BaseLevel = 1, BaseZ = 0,
                    Height = 2.8, TopZ = 2.8, Layers = [new ExchangeLayer { Function = "Structure", Width = 0.15, Material = "Repello" }]
                }
            });
        file.Elements.Add(new ExchangeElement
        {
            Id = 20, Category = "OST_Doors", Kind = "door", Family = "Puerta", Type = "0.90 x 2.10", Level = 1,
            Opening = new ExchangeOpening { Host = 10, Point = [2, 0, 0], Width = 0.9, Height = 2.1 }
        });
        file.Elements.Add(new ExchangeElement
        {
            Id = 21, Category = "OST_Windows", Kind = "window", Family = "Ventana", Type = "1.50 x 1.20", Level = 1,
            Opening = new ExchangeOpening { Host = 10, Point = [5.5, 0, 0.9], Width = 1.5, Height = 1.2, SillZ = 0.9 }
        });
        file.Elements.Add(new ExchangeElement
        {
            Id = 30, Category = "OST_Floors", Kind = "floor", Type = "Losa", Level = 1,
            Slab = new ExchangeSlab { Flat = true, Z = 0, Thickness = 0.12, Loops = [[[0, 0], [8, 0], [8, 5], [0, 5]]] }
        });
        file.Elements.Add(new ExchangeElement
        {
            Id = 40, Category = "OST_Furniture", Kind = "instance", Family = "Sofá", Type = "Tres plazas", Level = 1, Mesh = 0,
            Origin = [4, 3, 0]
        });
        File.WriteAllText(path, JsonSerializer.Serialize(file, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return path;
    }

    private static ExchangeMesh Box(Vector3 min, Vector3 max, int material)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = positions.Count;
            positions.AddRange([a, b, c, d]);
            indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }
        var (x0, y0, z0, x1, y1, z1) = (min.X, min.Y, min.Z, max.X, max.Y, max.Z);
        Face(new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0));
        Face(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1));
        Face(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1));
        Face(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0));
        Face(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0));
        Face(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1));
        var materials = Enumerable.Repeat(material, indices.Count / 3).ToArray();
        return new ExchangeMesh
        {
            Positions = Convert.ToBase64String(MemoryMarshal.AsBytes(positions.ToArray().AsSpan())),
            Indices = Convert.ToBase64String(MemoryMarshal.AsBytes(indices.ToArray().AsSpan())),
            Materials = Convert.ToBase64String(MemoryMarshal.AsBytes(materials.AsSpan()))
        };
    }
}
