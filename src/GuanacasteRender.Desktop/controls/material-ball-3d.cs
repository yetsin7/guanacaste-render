using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>An interactive, geometrically three-dimensional material preview, used only for the selected material.</summary>
internal sealed class MaterialBall3D : Viewport3D
{
    private readonly DiffuseMaterial _diffuse = new(new SolidColorBrush(Color.FromRgb(185, 185, 185)));
    private readonly AxisAngleRotation3D _rotation = new(new Vector3D(0, 1, 0), 0);
    private Point? _drag;

    public MaterialBall3D(double roughness, double metallic)
    {
        Width = 100; Height = 64; ClipToBounds = true;
        Camera = new PerspectiveCamera(new Point3D(0, 0, 4.7), new Vector3D(0, 0, -4.7), new Vector3D(0, 1, 0), 30);
        var model = new Model3DGroup();
        model.Children.Add(new AmbientLight(Color.FromRgb(95, 95, 95)));
        model.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2)));
        model.Children.Add(new DirectionalLight(Color.FromRgb(110, 135, 155), new Vector3D(2, .5, -1)));
        var material = new MaterialGroup(); material.Children.Add(_diffuse);
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(235, 235, 235)) { Opacity = Math.Clamp(.18 + metallic * .55 + (1 - roughness) * .27, 0, 1) }, Math.Clamp(8 + (1 - roughness) * 110, 8, 118)));
        var ball = new GeometryModel3D(Sphere(), material) { BackMaterial = material, Transform = new RotateTransform3D(_rotation) };
        model.Children.Add(ball); Children.Add(new ModelVisual3D { Content = model });
        ToolTip = "Arrastra para girar la muestra 3D";
        MouseLeftButtonDown += (_, e) => { _drag = e.GetPosition(this); CaptureMouse(); e.Handled = true; };
        MouseMove += (_, e) => { if (_drag is not { } last || e.LeftButton != MouseButtonState.Pressed) return; var point = e.GetPosition(this); _rotation.Angle += (point.X - last.X) * 2; _drag = point; };
        MouseLeftButtonUp += (_, e) => { if (_drag is null) return; _drag = null; ReleaseMouseCapture(); e.Handled = true; };
        LostMouseCapture += (_, _) => _drag = null;
    }

    public void SetTexture(BitmapSource bitmap) => _diffuse.Brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };

    private static MeshGeometry3D Sphere()
    {
        const int slices = 40, rings = 24;
        var mesh = new MeshGeometry3D();
        for (var y = 0; y <= rings; y++)
        {
            var latitude = Math.PI * y / rings;
            for (var x = 0; x <= slices; x++)
            {
                var longitude = 2 * Math.PI * x / slices;
                var normal = new Vector3D(Math.Sin(latitude) * Math.Cos(longitude), Math.Cos(latitude), Math.Sin(latitude) * Math.Sin(longitude));
                mesh.Positions.Add(new Point3D(normal.X, normal.Y, normal.Z)); mesh.Normals.Add(normal);
                mesh.TextureCoordinates.Add(new Point((double)x / slices, (double)y / rings));
            }
        }
        for (var y = 0; y < rings; y++) for (var x = 0; x < slices; x++)
        {
            var a = y * (slices + 1) + x; var b = a + slices + 1;
            mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b);
        }
        mesh.Freeze(); return mesh;
    }
}
