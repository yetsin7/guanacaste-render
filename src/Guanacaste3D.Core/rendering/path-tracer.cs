using System.Numerics;
using System.Runtime.CompilerServices;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Progressive, multi-threaded unidirectional path tracer for BIM scenes. Each call to
/// <see cref="RenderPass"/> adds one sample per active pixel to an accumulation buffer, so the
/// image converges while the user watches and can be stopped at any time.
/// </summary>
/// <remarks>
/// Surfaces are physically based: Lambert diffuse under a GGX specular coat, metals, mirrors and thin glass.
/// Light comes from a physical sky, the sun (sampled as a disk and combined with BSDF sampling through
/// multiple importance sampling), explicit lights and emissive geometry. Shadow rays pass through glass, so
/// sunlight enters through windows. Only the indirect part of the image is denoised; sun shadows, glass
/// and mirror reflections keep their sharpness.
/// </remarks>
public sealed partial class PathTracer
{
    private const int TileSize = 16;
    private const int MaxGlassCrossings = 10;
    private const float CameraSunRadianceLimit = 3000f;
    /// <summary>GGX alpha (roughness²) from which a glossy reflection counts as blurred light for denoising: roughness 0.3.</summary>
    private const float RoughReflectionAlpha = 0.09f;
    /// <summary>Largest luminance stored in the radiance cache, so one rare bright path cannot taint a whole cell.</summary>
    private const float CacheValueLimit = 30f;

    private readonly RenderScene _scene;
    private readonly Bvh _opaque;
    private readonly Bvh? _transmissive;
    // People who walk: their triangles are in no other hierarchy, and their vertices live in a copy of this tracer's own, so a
    // scene shared with other tracers is never moved. Hits in it are numbered from zero and shifted to the scene's numbering.
    private Bvh? _dynamic;
    private Vector3[]? _dynamicPositions;
    private int[]? _dynamicIndices;
    private Vector3[]? _dynamicNormals;
    private readonly int _dynamicTriangleStart;
    private readonly int _dynamicTriangleCount;
    private readonly SurfaceData[] _surfaces;
    private readonly float _rayEpsilon;
    private RenderSettings _settings;
    private SkyModel _sky = null!;
    private Vector3 _sunDirection;
    private float _sunCosine;
    private float _sunPdf;                    // 1 / solid angle of the sun disk
    private Vector3 _sunRadiance;             // radiance of the sun disk; zero when the sun is off
    private Vector3 _skyScale;
    private float _hazeDensity;               // extinction of aerial perspective per meter; zero turns it off
    private CloudLayer? _clouds;
    private Vector3 _cloudSunlight;           // sun irradiance over π that lights the clouds
    private Vector3 _cloudSkylight;           // zenith sky radiance that fills them
    private readonly Vector3 _sceneMin;
    private readonly Vector3 _sceneMax;
    private readonly SkyPortals? _portalCandidates;   // found once from the geometry; the setting only switches them on
    private readonly Emitters? _emitters;
    private SkyPortals? _portals;
    private LightSampler? _lightSampler;
    private Vector3[] _accumulation = [];
    private Vector3[] _direct = [];           // radiance gathered before the first diffuse bounce: sharp, low noise
    private float[] _luminanceSquares = [];
    private float[] _indirectSquares = [];    // squared luminance of the indirect component, for the denoiser's variance
    private ushort[] _counts = [];
    private bool[] _active = [];
    private RenderCamera _camera;
    private FeatureBuffer? _features;
    private bool _anyConverged;
    private long _rayCount;
    [ThreadStatic] private static long _threadRays;
    [ThreadStatic] private static long _threadCacheLookups;
    [ThreadStatic] private static long _threadCacheHits;
    private long _cacheLookups;
    private long _cacheHits;
    private RadianceCache? _radianceCache;
    private double _buildMilliseconds;
    private double _lastPassMilliseconds;
    private long _lastPassRays;
    private double _lastDenoiseMilliseconds;
    private PrimaryHit[]? _primary;

    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Number of completed passes (samples per pixel when adaptive sampling is off).</summary>
    public int SampleCount { get; private set; }
    public int TriangleCount => _scene.TriangleCount;
    /// <summary>Rays cast so far (camera, bounce and shadow rays), for throughput reporting.</summary>
    public long RayCount => Interlocked.Read(ref _rayCount);

    /// <summary>
    /// The acceleration structure over the opaque triangles (triangle numbers are those of the scene). Picking, collision
    /// and other queries can use it instead of building a second hierarchy over the same geometry. Read-only: the tracer
    /// owns it, so do not keep it beyond the tracer's lifetime.
    /// </summary>
    public Bvh OpaqueAcceleration => _opaque;

    /// <summary>The hierarchy over glass and other transmissive triangles, or null when the scene has none.</summary>
    public Bvh? TransmissiveAcceleration => _transmissive;

    /// <summary>
    /// Replaces the built-in denoiser, for example with an adapter for Open Image Denoise. Null (the default) uses the
    /// variance-guided filter configured by the render settings. Used when <see cref="RenderSettings.VarianceGuidedDenoising"/> is on.
    /// </summary>
    public IDenoiser? Denoiser { get; set; }

    /// <summary>The world-space radiance cache in use, or null when <see cref="RenderSettings.UseRadianceCache"/> is off.</summary>
    public RadianceCache? RadianceCache => _radianceCache;

    /// <summary>Share of cache lookups that found an entry and ended a path early (0 when the cache is off or unused).</summary>
    public double CacheHitRate => _cacheLookups == 0 ? 0 : (double)_cacheHits / _cacheLookups;

    /// <summary>Number of window panes sampled as sky portals (0 when the scene has none that help).</summary>
    public int SkyPortalCount => _portals?.Count ?? 0;

    /// <summary>Fraction of pixels still receiving samples; 1 until adaptive sampling retires some.</summary>
    public float ActiveFraction { get; private set; } = 1;

    /// <summary>Builds a tracer for the BIM mesh alone, using the stock material of each surface category.</summary>
    public PathTracer(SceneMesh mesh, int width, int height, RenderCamera camera, RenderSettings? settings = null)
        : this(new RenderSceneBuilder().AddBuilding(mesh ?? throw new ArgumentNullException(nameof(mesh))).Build(),
            width, height, camera, settings)
    {
    }

    /// <summary>Builds a tracer for a complete scene (BIM model, linked models and lights).</summary>
    public PathTracer(RenderScene scene, int width, int height, RenderCamera camera, RenderSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        RenderThreads.EnsureCapacity();
        if (width is < 1 or > 8192 || height is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(width), "Render size must be 1–8192 pixels.");
        var buildClock = System.Diagnostics.Stopwatch.StartNew();
        _scene = scene;
        Width = width;
        Height = height;
        _settings = settings ?? new RenderSettings();
        _camera = camera;

        _surfaces = scene.Materials.Select(SurfaceData.From).ToArray();
        var opaqueTriangles = new List<int>(scene.TriangleCount);
        var glassTriangles = new List<int>();
        _dynamicTriangleStart = scene.DynamicTriangleStart;
        _dynamicTriangleCount = scene.DynamicTriangleCount;
        var dynamicEnd = scene.DynamicTriangleCount == 0 ? 0 : scene.DynamicTriangleStart + scene.DynamicTriangleCount;
        for (var triangle = 0; triangle < scene.TriangleCount; triangle++)
        {
            if (triangle >= scene.DynamicTriangleStart && triangle < dynamicEnd) continue;
            (_surfaces[scene.TriangleMaterial[triangle]].Transmission > 0 ? glassTriangles : opaqueTriangles).Add(triangle);
        }
        _opaque = new Bvh(scene.Positions, scene.Indices, opaqueTriangles.ToArray(), cancellationToken) { Cutout = AlphaCutout.For(scene) };
        _transmissive = glassTriangles.Count == 0
            ? null
            : new Bvh(scene.Positions, scene.Indices, glassTriangles.ToArray(), cancellationToken);

        if (scene.DynamicTriangleCount > 0)
        {
            _dynamicPositions = scene.Positions.AsSpan(scene.DynamicVertexStart, scene.DynamicVertexCount).ToArray();
            _dynamicIndices = new int[scene.DynamicTriangleCount * 3];
            for (var index = 0; index < _dynamicIndices.Length; index++)
                _dynamicIndices[index] = scene.Indices[scene.DynamicTriangleStart * 3 + index] - scene.DynamicVertexStart;
            _dynamic = new Bvh(_dynamicPositions, _dynamicIndices, null);
            if (scene.Normals is { } sceneNormals)
                _dynamicNormals = sceneNormals.AsSpan(scene.DynamicVertexStart, scene.DynamicVertexCount).ToArray();
        }

        var (min, max) = scene.Bounds();
        var reach = MathF.Max(MathF.Max(MathF.Abs(min.X), MathF.Abs(max.X)),
            MathF.Max(MathF.Max(MathF.Abs(min.Y), MathF.Abs(max.Y)), MathF.Max(MathF.Abs(min.Z), MathF.Abs(max.Z))));
        _rayEpsilon = MathF.Max(1e-3f, reach * 4e-7f);

        _sceneMin = min;
        _sceneMax = max;
        _emitters = Emitters.TryCreate(scene, _surfaces, _opaque);
        // Windows become light portals so an interior is lit by the sky through them instead of by luck. They are
        // found once from the geometry; the SkyPortals setting only decides whether they are used.
        if (glassTriangles.Count > 0)
            _portalCandidates = SkyPortals.TryCreate(scene, glassTriangles, _opaque, _rayEpsilon);
        ApplySettings(_settings);
        RebuildWetness();
        RebuildGrass();
        _buildMilliseconds = buildClock.Elapsed.TotalMilliseconds;
        AllocateImageBuffers(width, height);
    }

    /// <summary>Derives everything that depends on lighting and quality settings; geometry structures are untouched.</summary>
    private void ApplySettings(RenderSettings settings)
    {
        _settings = settings;
        _sunDirection = Vector3.Normalize(settings.SunDirection.LengthSquared() > 1e-8f ? settings.SunDirection : Vector3.UnitZ);
        _sky = new SkyModel(settings.Sky, _sunDirection, settings.Turbidity);
        var radius = Math.Clamp(settings.SunAngularRadiusDegrees, 0.05f, 15f) * MathF.PI / 180;
        _sunCosine = MathF.Cos(radius);
        var solidAngle = 2 * MathF.PI * (1 - _sunCosine);
        _sunPdf = 1 / solidAngle;
        var sunColor = _sky.SunColor;
        _sunRadiance = settings.SunIntensity > 0
            ? sunColor * (MathF.PI * settings.SunIntensity / solidAngle)
            : Vector3.Zero;
        _skyScale = new Vector3(Math.Max(0, settings.SkyIntensity));
        _clouds = settings.Sky == SkyKind.Clear && settings.CloudCover > 0.001f ? new CloudLayer(settings.CloudCover, settings.CloudSeed, settings.Clouds, settings.CloudDirectionDegrees) : null;
        _cloudSunlight = settings.SunIntensity > 0 ? sunColor * settings.SunIntensity : Vector3.Zero;
        _cloudSkylight = _sky.Radiance(Vector3.UnitZ) * _skyScale;
        // Koschmieder: visibility V gives 2 % contrast left, so the extinction is 3.912 / V. Clear air (turbidity 2.5)
        // is taken as 12 km and hazier air shortens it in proportion.
        var visibility = 12000f * 2.5f / Math.Clamp(settings.Turbidity, 1.5f, 20f);
        _hazeDensity = float.IsFinite(settings.AerialPerspective)
            ? Math.Clamp(settings.AerialPerspective, 0f, 20f) * 3.912f / visibility
            : 0;
        _lightSampler = _scene.Lights.Length == 0
            ? null
            : new LightSampler(_scene.Lights, _sceneMin, _sceneMax,
                settings.LightCandidates > 0 ? settings.LightCandidates : int.MaxValue,
                settings.LightCandidates > 0 ? settings.LightCullThreshold : 0);
        _portals = settings.SkyPortals && _skyScale.X > 0 ? _portalCandidates : null;
        if (!settings.UseRadianceCache || settings.MaxBounces > 28)
        {
            _radianceCache = null;
        }
        else
        {
            var cell = settings.RadianceCacheCellSize > 0
                ? settings.RadianceCacheCellSize
                : Math.Clamp((_sceneMax - _sceneMin).Length() / 10f, 0.4f, 1.5f);
            if (_radianceCache is null || MathF.Abs(_radianceCache.CellSize - cell) > 1e-6f)
                _radianceCache = new RadianceCache(cell, CacheCapacityFor(_sceneMax - _sceneMin, cell))
                {
                    MinimumSamples = Math.Max(1, settings.RadianceCacheMinimumSamples),
                    WindowSamples = Math.Clamp(settings.RadianceCacheWindow, 4, 4000)
                };
        }
    }

    /// <summary>Table size from the surface area to cover: about one entry per cell per orientation, at most 22 MB.</summary>
    private static int CacheCapacityFor(Vector3 extent, float cell)
    {
        var area = 2 * (extent.X * extent.Y + extent.Y * extent.Z + extent.X * extent.Z);
        var cells = Math.Max(4096.0, area / (cell * cell) * 3);
        // Capped at 2^20 entries (22 MB): a huge ground plane would otherwise size the table for kilometers of surface nobody sees.
        return Math.Clamp((int)Math.Ceiling(Math.Log2(cells * 2)), 16, 20);
    }

    /// <summary>
    /// Forgets cached light inside a box, for example after an element moved or a luminaire changed, and restarts the
    /// image. Returns the number of cache entries dropped.
    /// </summary>
    public int InvalidateRegion(Vector3 min, Vector3 max)
    {
        var removed = _radianceCache?.InvalidateBox(min, max) ?? 0;
        Reset();
        return removed;
    }

    private void AllocateImageBuffers(int width, int height)
    {
        Width = width;
        Height = height;
        _accumulation = new Vector3[width * height];
        _direct = new Vector3[width * height];
        _luminanceSquares = new float[width * height];
        _indirectSquares = new float[width * height];
        _counts = new ushort[width * height];
        _active = new bool[width * height];
        Array.Fill(_active, true);
        SampleCount = 0;
        ActiveFraction = 1;
        _anyConverged = false;
        _features = null;
        _primary = null;
    }

    /// <summary>
    /// Applies new lighting or quality settings (sun, sky, exposure basis, bounces, light sampling, denoiser) and restarts
    /// accumulation. The acceleration structures, materials and image buffers are reused, so the cost is a few
    /// milliseconds even for a very large model. Call it from the same thread that calls <see cref="RenderPass"/>, never
    /// while a pass is running.
    /// </summary>
    public void UpdateSettings(RenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplySettings(settings);
        RebuildWetness();
        RebuildGrass();
        _radianceCache?.Clear();   // light changed, so what was cached no longer describes the scene
        Reset();
    }

    /// <summary>
    /// Changes the image size and restarts accumulation. Only the per-pixel buffers are reallocated; geometry is kept, so
    /// a live preview can change resolution without rebuilding anything. Not safe while a pass is running.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(width), "Render size must be 1–8192 pixels.");
        if (width == Width && height == Height)
        {
            Reset();
            return;
        }
        AllocateImageBuffers(width, height);
    }

    /// <summary>Changes the viewpoint and restarts accumulation (constant time apart from clearing the image buffers).</summary>
    public void SetCamera(RenderCamera camera)
    {
        _camera = camera;
        Reset();
    }

    /// <summary>
    /// Moves the walking people of the scene: <paramref name="positions"/> are all their vertices (as
    /// <see cref="ActorRig.Positions"/> gives them) and replace the ones the tracer holds. Only their small hierarchy is
    /// rebuilt, and the picture starts over, as it does when the camera moves.
    /// </summary>
    public void MoveActors(ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector3> normals = default)
    {
        if (_dynamicPositions is null || _dynamicIndices is null)
            throw new InvalidOperationException("The scene has no moving geometry.");
        if (positions.Length != _dynamicPositions.Length)
            throw new ArgumentException("The positions do not match the scene's moving geometry.", nameof(positions));
        positions.CopyTo(_dynamicPositions);
        // The smooth normals of cars turn with them; people have none, and their zeros stay.
        if (_dynamicNormals is not null && normals.Length == positions.Length) normals.CopyTo(_dynamicNormals);
        _dynamic = new Bvh(_dynamicPositions, _dynamicIndices, null);
        _primary = null;
        Reset();
    }

    /// <summary>Whether the scene has people who can be moved with <see cref="MoveActors"/>.</summary>
    public bool HasActors => _dynamicPositions is not null;

    /// <summary>Discards accumulated samples and the cached denoising features.</summary>
    public void Reset()
    {
        Array.Clear(_accumulation);
        Array.Clear(_direct);
        Array.Clear(_luminanceSquares);
        Array.Clear(_indirectSquares);
        Array.Clear(_counts);
        Array.Fill(_active, true);
        SampleCount = 0;
        ActiveFraction = 1;
        _anyConverged = false;
        _features = null;
    }

    /// <summary>Mean samples per pixel actually taken, which differs from <see cref="SampleCount"/> under adaptive sampling.</summary>
    public double AverageSamplesPerPixel
    {
        get
        {
            long total = 0;
            foreach (var count in _counts) total += count;
            return (double)total / _counts.Length;
        }
    }

    // ---------------------------------------------------------------- camera

    private readonly record struct CameraBasis(Vector3 Forward, Vector3 Right, Vector3 Up, float HalfWidth,
        float HalfHeight, float VerticalShift, float HorizontalShift = 0);

    /// <summary>Angle one pixel spans (radians, small-angle), so distance times it is a pixel's size on a surface.</summary>
    private float PixelAngle =>
        2 * MathF.Tan(Math.Clamp(_camera.VerticalFieldOfViewDegrees, 5f, 150f) * MathF.PI / 360) / Height;

    /// <summary>
    /// Returns the camera frame; with vertical correction the image plane stays upright and is shifted instead. The lens
    /// can also be rolled about the view axis and shifted by hand (a shift lens), as in a photographic camera.
    /// </summary>
    private CameraBasis Basis()
    {
        var forward = Vector3.Normalize(_camera.Target - _camera.Position);
        var halfHeight = MathF.Tan(Math.Clamp(_camera.VerticalFieldOfViewDegrees, 5f, 150f) * MathF.PI / 360);
        var halfWidth = halfHeight * Width / Height;
        CameraBasis basis;
        if (_camera.CorrectVerticals && !_camera.Panoramic && MathF.Abs(forward.Z) < 0.97f)
        {
            var horizontal = Vector3.Normalize(new Vector3(forward.X, forward.Y, 0));
            var rightLevel = Vector3.Normalize(Vector3.Cross(horizontal, Vector3.UnitZ));
            basis = new CameraBasis(horizontal, rightLevel, Vector3.UnitZ, halfWidth, halfHeight,
                forward.Z / MathF.Sqrt(1 - forward.Z * forward.Z));
        }
        else
        {
            var worldUp = MathF.Abs(forward.Z) > 0.999f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(forward, worldUp));
            basis = new CameraBasis(forward, right, Vector3.Cross(right, forward), halfWidth, halfHeight, 0);
        }
        if (_camera.RollDegrees != 0)
        {
            // Turning the sensor about the view axis tilts the horizon without moving the camera.
            var roll = _camera.RollDegrees * MathF.PI / 180;
            var (sin, cos) = MathF.SinCos(roll);
            basis = basis with { Right = basis.Right * cos + basis.Up * sin, Up = basis.Up * cos - basis.Right * sin };
        }
        if (_camera.Panoramic) return basis;
        // Shifts are fractions of the frame height: the sensor slides behind the lens, so lines stay parallel.
        return basis with
        {
            VerticalShift = basis.VerticalShift + _camera.ShiftY * 2 * halfHeight,
            HorizontalShift = _camera.ShiftX * 2 * halfHeight
        };
    }

    /// <summary>
    /// The direction through a point of the image (x and y from −1 to 1): a flat sensor for an ordinary lens, or the
    /// longitude and latitude of an equirectangular 360° panorama.
    /// </summary>
    private Vector3 ViewDirection(in CameraBasis basis, float x, float y)
    {
        if (!_camera.Panoramic)
            return basis.Forward + basis.Right * (x * basis.HalfWidth + basis.HorizontalShift) +
                   basis.Up * (y * basis.HalfHeight + basis.VerticalShift);
        var longitude = x * MathF.PI;
        var latitude = y * MathF.PI / 2;
        var (sinLon, cosLon) = MathF.SinCos(longitude);
        var (sinLat, cosLat) = MathF.SinCos(latitude);
        return basis.Forward * (cosLat * cosLon) + basis.Right * (cosLat * sinLon) + basis.Up * sinLat;
    }

    /// <summary>Creates the primary ray through continuous pixel coordinates, including thin-lens blur.</summary>
    private (Vector3 Origin, Vector3 Direction) PrimaryRay(in CameraBasis basis, float column, float row,
        ref Sampler sampler)
    {
        var x = 2 * column / Width - 1;
        var y = 1 - 2 * row / Height;
        var unscaled = ViewDirection(basis, x, y);
        if (_camera.ApertureRadius <= 0 || _camera.Panoramic) return (_camera.Position, Vector3.Normalize(unscaled));
        var lens = Sampling.Disk(sampler.NextFloat(), sampler.NextFloat()) * _camera.ApertureRadius;
        var origin = _camera.Position + basis.Right * lens.X + basis.Up * lens.Y;
        var focus = _camera.Position + unscaled * _camera.EffectiveFocusDistance;
        return (origin, Vector3.Normalize(focus - origin));
    }

    // ---------------------------------------------------------------- features for the denoiser

    /// <summary>Casts one pixel-centre ray per pixel to record first-hit normal, albedo and depth, looking through glass.</summary>
    public FeatureBuffer CaptureFeatures()
    {
        if (_features is not null) return _features;
        var features = new FeatureBuffer(Width, Height) { MaterialNames = _scene.Materials.Select(material => material.Name).ToArray() };
        var basis = Basis();
        Parallel.For(0, Height, row =>
        {
            var sampler = new Sampler((uint)row, 0, 0);
            for (var column = 0; column < Width; column++)
            {
                var index = row * Width + column;
                var camera = _camera with { ApertureRadius = 0 };
                var x = 2 * (column + 0.5f) / Width - 1;
                var y = 1 - 2 * (row + 0.5f) / Height;
                var direction = Vector3.Normalize(ViewDirection(basis, x, y));
                var origin = camera.Position;
                var found = false;
                var travelled = 0f;
                for (var crossing = 0; crossing < MaxGlassCrossings; crossing++)
                {
                    if (!Closest(origin, direction, float.MaxValue, out var hit, out var bvh)) break;
                    travelled += hit.Distance;
                    ref readonly var surface = ref _surfaces[_scene.TriangleMaterial[hit.Triangle]];
                    var position = origin + direction * hit.Distance;
                    if (surface.Transmission >= 0.5f)
                    {
                        origin = position + direction * _rayEpsilon;
                        continue;
                    }
                    var point = Shade(hit, bvh, position, direction, detailed: true);
                    features.Normal[index] = point.Normal;
                    features.Albedo[index] = point.Diffuse;
                    features.Depth[index] = travelled;
                    features.Material[index] = (ushort)Math.Min(_scene.TriangleMaterial[hit.Triangle], FeatureBuffer.NoMaterial - 1);
                    found = true;
                    break;
                }
                if (!found) features.Depth[index] = float.PositiveInfinity;
            }
        });
        return _features = features;
    }

    // ---------------------------------------------------------------- passes

    /// <summary>Adds one jittered sample to every active pixel, using all available cores.</summary>
    public void RenderPass(CancellationToken cancellationToken = default)
    {
        var passClock = System.Diagnostics.Stopwatch.StartNew();
        var raysBefore = RayCount;
        var basis = Basis();
        var tilesX = (Width + TileSize - 1) / TileSize;
        var tilesY = (Height + TileSize - 1) / TileSize;
        var seed = _settings.Seed * 2654435761u;
        var tileCount = tilesX * tilesY;
        Parallel.For(0, tileCount, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = _settings.MaxThreads > 0 ? _settings.MaxThreads : -1
        }, tile =>
        {
            var tileX = tile % tilesX * TileSize;
            var tileY = tile / tilesX * TileSize;
            var endX = Math.Min(Width, tileX + TileSize);
            var endY = Math.Min(Height, tileY + TileSize);
            for (var row = tileY; row < endY; row++)
            for (var column = tileX; column < endX; column++)
                SamplePixel(row * Width + column, column, row, basis, seed);
            Interlocked.Add(ref _rayCount, _threadRays);
            _threadRays = 0;
            if (_threadCacheLookups != 0)
            {
                Interlocked.Add(ref _cacheLookups, _threadCacheLookups);
                Interlocked.Add(ref _cacheHits, _threadCacheHits);
                _threadCacheLookups = _threadCacheHits = 0;
            }
        });
        SampleCount++;
        if (_settings.AdaptiveSampling && SampleCount >= _settings.AdaptiveMinimumSamples) RetireConvergedPixels();
        _lastPassMilliseconds = passClock.Elapsed.TotalMilliseconds;
        _lastPassRays = RayCount - raysBefore;
    }

    /// <summary>Adds one jittered sample to a pixel if it is still active.</summary>
    private void SamplePixel(int index, int column, int row, CameraBasis basis, uint seed)
    {
        if (!_active[index] || _counts[index] == ushort.MaxValue) return;
        var sampler = new Sampler((uint)index, _counts[index], seed);
        var jitter = sampler.JitterPair();
        // Tent-shaped reconstruction filter about a pixel wide in each direction: smooth edges without blur.
        var offsetX = Tent(jitter.X);
        var offsetY = Tent(jitter.Y);
        var (origin, direction) = PrimaryRay(basis, column + 0.5f + offsetX, row + 0.5f + offsetY, ref sampler);
        var (direct, indirect) = Trace(origin, direction, ref sampler);
        var total = direct + indirect;
        if (!float.IsFinite(total.X + total.Y + total.Z))
        {
            direct = indirect = total = Vector3.Zero;
        }
        _accumulation[index] += total;
        _direct[index] += direct;
        var luminance = Sampling.Luminance(total);
        _luminanceSquares[index] += luminance * luminance;
        var indirectLuminance = Sampling.Luminance(indirect);
        _indirectSquares[index] += indirectLuminance * indirectLuminance;
        _counts[index]++;
    }

    /// <summary>
    /// Renders one rectangle of the image to completion, like a V-Ray bucket: samples are added to just these pixels until
    /// <paramref name="targetSamples"/> is reached or, with adaptive sampling, every pixel of the bucket has converged.
    /// Different buckets touch different pixels, so they can be rendered from several threads at once.
    /// </summary>
    public void RenderBucket(int x, int y, int width, int height, int targetSamples, CancellationToken cancellationToken = default)
    {
        var basis = Basis();
        var seed = _settings.Seed * 2654435761u;
        var endX = Math.Min(Width, x + width);
        var endY = Math.Min(Height, y + height);
        try
        {
            for (var sample = 1; sample <= targetSamples; sample++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var row = y; row < endY; row++)
                for (var column = x; column < endX; column++)
                    SamplePixel(row * Width + column, column, row, basis, seed);
                if (_settings.AdaptiveSampling && sample >= _settings.AdaptiveMinimumSamples &&
                    RetireConvergedPixels(x, y, endX, endY)) break;
            }
        }
        finally
        {
            Interlocked.Add(ref _rayCount, _threadRays);
            _threadRays = 0;
            if (_threadCacheLookups != 0)
            {
                Interlocked.Add(ref _cacheLookups, _threadCacheLookups);
                Interlocked.Add(ref _cacheHits, _threadCacheHits);
                _threadCacheLookups = _threadCacheHits = 0;
            }
        }
    }

    /// <summary>
    /// Marks the sample count after a bucketed render, so images and statistics treat it like a progressive one. Passing
    /// the target when every bucket is done also refreshes the share of pixels still active.
    /// </summary>
    public void ReportBucketedSamples(int samples, bool refreshActive)
    {
        SampleCount = Math.Max(SampleCount, samples);
        if (!refreshActive) return;
        var active = 0;
        foreach (var flag in _active) if (flag) active++;
        _anyConverged = active < _counts.Length;
        ActiveFraction = (float)active / _counts.Length;
    }

    /// <summary>Retires converged pixels inside one bucket; returns true when none of its pixels need more samples.</summary>
    private bool RetireConvergedPixels(int startX, int startY, int endX, int endY)
    {
        var width = endX - startX;
        var height = endY - startY;
        var converged = new bool[width * height];
        var tolerance = _settings.AdaptiveTolerance;
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
        {
            var index = (startY + row) * Width + startX + column;
            var count = _counts[index];
            if (count < _settings.AdaptiveMinimumSamples) continue;
            var mean = Sampling.Luminance(_accumulation[index]) / count;
            var variance = MathF.Max(0, _luminanceSquares[index] / count - mean * mean);
            var error = MathF.Sqrt(variance / count) / (mean + 0.08f);
            converged[row * width + column] = error < tolerance;
        }
        var anyActive = false;
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
        {
            var keep = !converged[row * width + column];
            for (var dy = -1; dy <= 1 && !keep; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = column + dx;
                var ny = row + dy;
                if ((uint)nx < (uint)width && (uint)ny < (uint)height && !converged[ny * width + nx]) { keep = true; break; }
            }
            _active[(startY + row) * Width + startX + column] = keep;
            anyActive |= keep;
        }
        return !anyActive;
    }

    /// <summary>Maps a uniform value in [0, 1) to a triangular distribution on (−0.8, 0.8) pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Tent(float u) => 0.8f * (u < 0.5f ? MathF.Sqrt(2 * u) - 1 : 1 - MathF.Sqrt(2 - 2 * u));

    /// <summary>
    /// Marks pixels converged when the relative standard error of their luminance falls below the tolerance, then
    /// keeps every pixel next to an unconverged one active so edges between noisy and settled areas stay coherent.
    /// </summary>
    private void RetireConvergedPixels()
    {
        var converged = new bool[_counts.Length];
        var tolerance = _settings.AdaptiveTolerance;
        Parallel.For(0, Height, row =>
        {
            for (var column = 0; column < Width; column++)
            {
                var index = row * Width + column;
                var count = _counts[index];
                if (count < _settings.AdaptiveMinimumSamples) continue;
                var mean = Sampling.Luminance(_accumulation[index]) / count;
                var variance = MathF.Max(0, _luminanceSquares[index] / count - mean * mean);
                // Bias toward the mean so near-black pixels do not demand endless samples.
                var error = MathF.Sqrt(variance / count) / (mean + 0.08f);
                converged[index] = error < tolerance;
            }
        });
        var active = 0;
        for (var row = 0; row < Height; row++)
        for (var column = 0; column < Width; column++)
        {
            var index = row * Width + column;
            var keep = !converged[index];
            if (!keep)
            {
                for (var dy = -1; dy <= 1 && !keep; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = column + dx;
                    var ny = row + dy;
                    if ((uint)nx < (uint)Width && (uint)ny < (uint)Height && !converged[ny * Width + nx])
                    {
                        keep = true;
                        break;
                    }
                }
            }
            _active[index] = keep;
            if (keep) active++;
        }
        _anyConverged = active < _counts.Length;
        ActiveFraction = (float)active / _counts.Length;
    }

    // ---------------------------------------------------------------- image output

    /// <summary>
    /// Variance of the mean indirect luminance of each pixel (sample variance divided by the sample count). Pixels with
    /// fewer than three samples report a large value, so a denoiser treats them as unreliable.
    /// </summary>
    public float[] IndirectVarianceOfMean()
    {
        var variance = new float[_accumulation.Length];
        FillIndirectVariance(variance);
        return variance;
    }

    private void FillIndirectVariance(float[] variance)
    {
        for (var index = 0; index < _accumulation.Length; index++)
        {
            var count = _counts[index];
            if (count < 3) { variance[index] = 1e3f; continue; }
            var mean = Sampling.Luminance(_accumulation[index] - _direct[index]) / count;
            var perSample = MathF.Max(0, _indirectSquares[index] / count - mean * mean) * count / (count - 1);
            variance[index] = perSample / count;
        }
    }

    /// <summary>Average over pixels of (standard error of the indirect estimate) / (its brightness + a floor); a gauge of how noisy the image still is.</summary>
    public float MeanRelativeNoise() => MeanRelativeNoise(LinearComponents().Indirect, IndirectVarianceOfMean());

    private float MeanRelativeNoise(Vector3[] indirect, float[] variance)
    {
        double sum = 0;
        var used = 0;
        for (var index = 0; index < _counts.Length; index++)
        {
            if (_counts[index] < 3) continue;
            sum += Math.Min(2.0, Math.Sqrt(variance[index]) / (Sampling.Luminance(indirect[index]) + 0.01));
            used++;
        }
        return used == 0 ? 1f : (float)(sum / used);
    }

    /// <summary>Returns the mean direct and indirect radiance separately (render passes / AOVs).</summary>
    public (Vector3[] Direct, Vector3[] Indirect) LinearComponents()
    {
        var direct = new Vector3[_accumulation.Length];
        var indirect = new Vector3[_accumulation.Length];
        for (var index = 0; index < direct.Length; index++)
        {
            var count = _counts[index];
            if (count == 0) continue;
            direct[index] = _direct[index] / count;
            indirect[index] = (_accumulation[index] - _direct[index]) / count;
        }
        return (direct, indirect);
    }

    /// <summary>
    /// Returns the mean linear radiance of every pixel. When denoising, only the indirect
    /// component is filtered, so sun shadows and directly seen sky stay sharp.
    /// </summary>
    public Vector3[] LinearImage(bool denoise)
    {
        if (denoise && SampleCount > 0 && _settings.VarianceGuidedDenoising) return GuidedImage();
        var image = new Vector3[_accumulation.Length];
        if (SampleCount == 0) return image;
        if (!denoise)
        {
            for (var index = 0; index < image.Length; index++)
                if (_counts[index] > 0) image[index] = _accumulation[index] / _counts[index];
            return image;
        }
        for (var index = 0; index < image.Length; index++)
            if (_counts[index] > 0) image[index] = (_accumulation[index] - _direct[index]) / _counts[index];
        var denoiseClock = System.Diagnostics.Stopwatch.StartNew();
        var filtered = AtrousDenoiser.Apply(image, CaptureFeatures());
        // Many samples need little filtering; blend back toward the raw estimate so detail is not softened for nothing.
        var strength = DenoiseStrength();
        for (var index = 0; index < image.Length; index++)
        {
            if (_counts[index] == 0) continue;
            var raw = image[index];
            filtered[index] = Vector3.Lerp(raw, filtered[index], strength) + _direct[index] / _counts[index];
        }
        _lastDenoiseMilliseconds = denoiseClock.Elapsed.TotalMilliseconds;
        return filtered;
    }

    /// <summary>
    /// The variance-guided denoised image. Its working buffers come from a pool, so producing a frame allocates one
    /// image-sized array (the result) instead of a dozen.
    /// </summary>
    private Vector3[] GuidedImage()
    {
        var count = _accumulation.Length;
        var denoiseClock = System.Diagnostics.Stopwatch.StartNew();
        var image = System.Buffers.ArrayPool<Vector3>.Shared.Rent(count);
        var variance = System.Buffers.ArrayPool<float>.Shared.Rent(count);
        try
        {
            for (var index = 0; index < count; index++)
                image[index] = _counts[index] > 0 ? (_accumulation[index] - _direct[index]) / _counts[index] : Vector3.Zero;
            FillIndirectVariance(variance);
            var noise = MeanRelativeNoise(image, variance);
            var denoiser = Denoiser ?? new VarianceGuidedDenoiser(_settings.DenoiserLumaSigmaFewSamples, _settings.DenoiserLumaSigma,
                _settings.DenoiserCleanNoise, _settings.DenoiserNoisyNoise, _settings.DenoiserIterations);
            var guided = denoiser.Denoise(image, variance, CaptureFeatures(), noise);
            for (var index = 0; index < count; index++)
                if (_counts[index] > 0) guided[index] += _direct[index] / _counts[index];
            _lastDenoiseMilliseconds = denoiseClock.Elapsed.TotalMilliseconds;
            return guided;
        }
        finally
        {
            System.Buffers.ArrayPool<Vector3>.Shared.Return(image);
            System.Buffers.ArrayPool<float>.Shared.Return(variance);
        }
    }

    /// <summary>1 at low sample counts, easing to 0.55 once the image is already clean.</summary>
    private float DenoiseStrength()
    {
        var samples = (float)AverageSamplesPerPixel;
        return samples <= 64 ? 1f : Math.Clamp(1 - 0.45f * (samples - 64) / 448f, 0.55f, 1f);
    }

    /// <summary>Returns tone-mapped, gamma-encoded pixels in BGRA32 order for display.</summary>
    public byte[] ToBgra32(bool denoise = false, ImageFinish? finish = null) =>
        ImageProcessor.ToBgra32(LinearImage(denoise), Width, Height, _settings.Exposure, WithScreenEffects(finish ?? ImageProcessor.Plain),
            CaptureFeatures);

    /// <summary>The camera's view as a projection of world points onto the picture (null for a panorama, which has no flat picture).</summary>
    public ScreenProjection? Projection()
    {
        if (_camera.Panoramic) return null;
        var basis = Basis();
        return new ScreenProjection(_camera.Position, basis.Forward, basis.Right, basis.Up, basis.HalfWidth, basis.HalfHeight,
            basis.HorizontalShift, basis.VerticalShift, Width, Height);
    }

    /// <summary>
    /// The finish with the particles of its fires and smoke placed on this tracer's picture: they depend on where the camera
    /// is and how big the picture is, so the tracer that knows both does it. A finish without fires comes back as it was.
    /// </summary>
    public ImageFinish WithScreenEffects(ImageFinish finish)
    {
        if (finish.Fires is not { Count: > 0 } fires || Projection() is not { } view) return finish;
        return finish with { FireSprites = FireParticles.Build(fires, view, finish.FireSeconds) };
    }

    /// <summary>Returns the linear (pre-tone-mapping) mean radiance for one pixel.</summary>
    public Vector3 LinearPixel(int column, int row)
    {
        var index = row * Width + column;
        return _counts[index] == 0 ? Vector3.Zero : _accumulation[index] / _counts[index];
    }

    /// <summary>Base exposure multiplier from the settings, applied before <see cref="ImageFinish"/>.</summary>
    public float BaseExposure => _settings.Exposure;

    // ---------------------------------------------------------------- telemetry and diagnostics

    /// <summary>A snapshot of the sizes, costs and cache state of this renderer, for the developer panel.</summary>
    public RenderTelemetry Telemetry()
    {
        var imageBytes = RenderSettings.EstimateImageBytes(Width, Height);
        return new RenderTelemetry(Width, Height, TriangleCount, _scene.Lights.Length, _emitters?.Count ?? 0, SkyPortalCount,
            AverageSamplesPerPixel, ActiveFraction, RayCount,
            _lastPassMilliseconds > 0 ? _lastPassRays / _lastPassMilliseconds / 1000 : 0,
            _lastPassMilliseconds, _lastDenoiseMilliseconds, _buildMilliseconds, imageBytes, _scene.EstimatedBytes,
            _radianceCache?.MemoryBytes ?? 0, CacheHitRate, _radianceCache?.OccupiedEntries() ?? 0, GC.GetTotalMemory(false),
            SampleCount == 0 ? 1 : MeanRelativeNoise());
    }

    /// <summary>The first surface seen through each pixel centre, with what the diagnostic views need.</summary>
    private struct PrimaryHit
    {
        public Vector3 Position;
        public Vector3 Normal;
        public float Depth;
        public int Material;
    }

    private PrimaryHit[] PrimaryHits()
    {
        if (_primary is { } cached) return cached;
        var hits = new PrimaryHit[Width * Height];
        var basis = Basis();
        Parallel.For(0, Height, row =>
        {
            for (var column = 0; column < Width; column++)
            {
                var index = row * Width + column;
                var x = 2 * (column + 0.5f) / Width - 1;
                var y = 1 - 2 * (row + 0.5f) / Height;
                var direction = Vector3.Normalize(ViewDirection(basis, x, y));
                var origin = _camera.Position;
                var travelled = 0f;
                hits[index].Depth = float.PositiveInfinity;
                for (var crossing = 0; crossing < MaxGlassCrossings; crossing++)
                {
                    if (!Closest(origin, direction, float.MaxValue, out var hit, out var bvh)) break;
                    travelled += hit.Distance;
                    var position = origin + direction * hit.Distance;
                    var material = _scene.TriangleMaterial[hit.Triangle];
                    if (_surfaces[material].Transmission >= 0.5f)
                    {
                        origin = position + direction * _rayEpsilon;
                        continue;
                    }
                    var point = Shade(hit, bvh, position, direction, detailed: false);
                    hits[index] = new PrimaryHit { Position = position, Normal = point.Normal, Depth = travelled, Material = material };
                    break;
                }
            }
        });
        return _primary = hits;
    }

    /// <summary>
    /// Renders one diagnostic view as display-ready colors in [0, 1]. Views that read the accumulated image report
    /// zero until at least one pass has run.
    /// </summary>
    public Vector3[] Diagnostic(DiagnosticView view)
    {
        var hits = PrimaryHits();
        var image = new Vector3[Width * Height];
        var features = CaptureFeatures();
        var sky = new Vector3(0.08f, 0.09f, 0.11f);

        float Percentile95(Func<int, float> value, Func<int, bool> valid, float fallback)
        {
            var sample = new List<float>();
            for (var i = 0; i < hits.Length; i += Math.Max(1, hits.Length / 4096)) if (valid(i)) sample.Add(value(i));
            return sample.Count == 0 ? fallback : sample.OrderBy(x => x).ElementAt((int)((sample.Count - 1) * 0.95));
        }
        static Vector3 Gamma(Vector3 linear) => new(MathF.Pow(Math.Clamp(linear.X, 0, 1), 1 / 2.2f), MathF.Pow(Math.Clamp(linear.Y, 0, 1), 1 / 2.2f), MathF.Pow(Math.Clamp(linear.Z, 0, 1), 1 / 2.2f));
        Vector3 Tone(Vector3 radiance) { var v = radiance * _settings.Exposure; return Gamma(v / (Vector3.One + v)); }

        switch (view)
        {
            case DiagnosticView.Normals:
                for (var i = 0; i < image.Length; i++) image[i] = float.IsInfinity(hits[i].Depth) ? sky : hits[i].Normal * 0.5f + new Vector3(0.5f);
                break;
            case DiagnosticView.Albedo:
                for (var i = 0; i < image.Length; i++) image[i] = float.IsInfinity(hits[i].Depth) ? sky : Gamma(features.Albedo[i]);
                break;
            case DiagnosticView.Depth:
            {
                var near = float.MaxValue;
                foreach (var hit in hits) if (!float.IsInfinity(hit.Depth)) near = MathF.Min(near, hit.Depth);
                var far = Percentile95(i => hits[i].Depth, i => !float.IsInfinity(hits[i].Depth), near * 4);
                var span = MathF.Log2(MathF.Max(far / MathF.Max(near, 1e-3f), 1.001f));
                for (var i = 0; i < image.Length; i++)
                    image[i] = float.IsInfinity(hits[i].Depth) ? sky : new Vector3(1 - Math.Clamp(MathF.Log2(hits[i].Depth / MathF.Max(near, 1e-3f)) / span, 0f, 1f));
                break;
            }
            case DiagnosticView.WorldPosition:
                for (var i = 0; i < image.Length; i++)
                {
                    var p = hits[i].Position;
                    var q = p + new Vector3(0.37f); // offset so walls on whole-meter planes do not flicker between 0 and 1
                    image[i] = float.IsInfinity(hits[i].Depth) ? sky : new Vector3(q.X - MathF.Floor(q.X), q.Y - MathF.Floor(q.Y), q.Z - MathF.Floor(q.Z));
                }
                break;
            case DiagnosticView.MaterialId:
                for (var i = 0; i < image.Length; i++) image[i] = float.IsInfinity(hits[i].Depth) ? sky : DiagnosticColors.Id(hits[i].Material);
                break;
            case DiagnosticView.Roughness:
                for (var i = 0; i < image.Length; i++) image[i] = float.IsInfinity(hits[i].Depth) ? sky : new Vector3(_scene.Materials[hits[i].Material].Roughness);
                break;
            case DiagnosticView.Metallic:
                for (var i = 0; i < image.Length; i++) image[i] = float.IsInfinity(hits[i].Depth) ? sky : new Vector3(_scene.Materials[hits[i].Material].Metallic);
                break;
            case DiagnosticView.SampleCount:
            {
                var most = (float)Math.Max(1, _counts.Max(count => (int)count));
                for (var i = 0; i < image.Length; i++) image[i] = DiagnosticColors.Heat(_counts[i] / most);
                break;
            }
            case DiagnosticView.Noise:
            case DiagnosticView.DenoiserConfidence:
            {
                var (_, indirect) = LinearComponents();
                var variance = IndirectVarianceOfMean();
                for (var i = 0; i < image.Length; i++)
                {
                    var noise = _counts[i] < 3 ? 2f : MathF.Min(2f, MathF.Sqrt(variance[i]) / (Sampling.Luminance(indirect[i]) + 0.01f));
                    image[i] = view == DiagnosticView.Noise ? DiagnosticColors.Heat(noise / 0.5f) : new Vector3(1 / (1 + noise * 4));
                }
                break;
            }
            case DiagnosticView.LightComplexity:
            {
                var counts = new int[image.Length];
                for (var i = 0; i < image.Length; i++) counts[i] = _lightSampler is null || float.IsInfinity(hits[i].Depth) ? 0 : _lightSampler.LightsNear(hits[i].Position);
                var most = (float)Math.Max(1, counts.Max());
                for (var i = 0; i < image.Length; i++) image[i] = DiagnosticColors.Heat(counts[i] / most);
                break;
            }
            case DiagnosticView.CacheResidency:
                for (var i = 0; i < image.Length; i++)
                    image[i] = _radianceCache is not null && !float.IsInfinity(hits[i].Depth) && _radianceCache.TryGet(hits[i].Position, hits[i].Normal, out _)
                        ? Vector3.One : new Vector3(0.1f, 0f, 0f);
                break;
            case DiagnosticView.PortalReach:
            {
                var reach = new float[image.Length];
                for (var i = 0; i < image.Length; i++) reach[i] = _portals is null || float.IsInfinity(hits[i].Depth) ? 0 : _portals.Reach(hits[i].Position, hits[i].Normal);
                var most = Math.Max(reach.Max(), 1e-6f);
                for (var i = 0; i < image.Length; i++) image[i] = DiagnosticColors.Heat(MathF.Sqrt(reach[i] / most));
                break;
            }
            case DiagnosticView.DirectLight:
            case DiagnosticView.IndirectLight:
            {
                var (direct, indirect) = LinearComponents();
                var source = view == DiagnosticView.DirectLight ? direct : indirect;
                for (var i = 0; i < image.Length; i++) image[i] = Tone(source[i]);
                break;
            }
        }
        return image;
    }

    /// <summary>The same view as <see cref="Diagnostic"/> encoded as BGRA32 for display.</summary>
    public byte[] DiagnosticBgra32(DiagnosticView view)
    {
        var image = Diagnostic(view);
        var bytes = new byte[image.Length * 4];
        for (var i = 0; i < image.Length; i++)
        {
            bytes[i * 4] = (byte)(Math.Clamp(image[i].Z, 0f, 1f) * 255 + 0.5f);
            bytes[i * 4 + 1] = (byte)(Math.Clamp(image[i].Y, 0f, 1f) * 255 + 0.5f);
            bytes[i * 4 + 2] = (byte)(Math.Clamp(image[i].X, 0f, 1f) * 255 + 0.5f);
            bytes[i * 4 + 3] = 255;
        }
        return bytes;
    }

    // ---------------------------------------------------------------- light transport

    /// <summary>Finds the nearest hit among opaque and transmissive geometry.</summary>
    private bool Closest(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit, out Bvh bvh)
    {
        _threadRays++;
        hit = new RayHit { Distance = maxDistance };
        bvh = _opaque;
        var found = _opaque.Intersect(origin, direction, _rayEpsilon * 0.1f, ref hit);
        if (_dynamic is not null && _dynamic.Intersect(origin, direction, _rayEpsilon * 0.1f, ref hit))
        {
            hit.Triangle += _dynamicTriangleStart;
            bvh = _dynamic;
            found = true;
        }
        if (_transmissive is not null && _transmissive.Intersect(origin, direction, _rayEpsilon * 0.1f, ref hit))
        {
            bvh = _transmissive;
            found = true;
        }
        return found;
    }

    /// <summary>The state of a path vertex that shading needs.</summary>
    private readonly record struct ShadePoint(Vector3 Position, Vector3 Geometric, Vector3 Normal, Vector3 Diffuse,
        int Material, SurfaceData Surface);

    /// <summary>Computes facing normals and the pattern-modulated diffuse color at a hit; <paramref name="detailed"/> evaluates the pattern exactly.</summary>
    private ShadePoint Shade(in RayHit hit, Bvh bvh, Vector3 position, Vector3 direction, bool detailed)
    {
        var triangle = hit.Triangle;
        var material = _scene.TriangleMaterial[triangle];
        var geometric = bvh.Normal(in hit);
        var normal = geometric;
        var smooth = Vector3.Zero;
        if (_dynamicNormals is not null && triangle >= _dynamicTriangleStart && triangle < _dynamicTriangleStart + _dynamicTriangleCount)
        {
            // A moving car: its smooth normals live in this tracer's own copy, turned with the car.
            var i = (triangle - _dynamicTriangleStart) * 3;
            smooth = _dynamicNormals[_dynamicIndices![i]] * (1 - hit.U - hit.V) + _dynamicNormals[_dynamicIndices[i + 1]] * hit.U +
                     _dynamicNormals[_dynamicIndices[i + 2]] * hit.V;
        }
        else if (_scene.Normals is { } normals)
        {
            var i = triangle * 3;
            smooth = normals[_scene.Indices[i]] * (1 - hit.U - hit.V) + normals[_scene.Indices[i + 1]] * hit.U +
                     normals[_scene.Indices[i + 2]] * hit.V;
        }
        if (smooth.LengthSquared() > 1e-6f)
        {
            smooth = Vector3.Normalize(smooth);
            normal = Vector3.Dot(smooth, geometric) < 0 ? -smooth : smooth;
        }
        if (Vector3.Dot(geometric, direction) > 0)
        {
            geometric = -geometric;
            normal = -normal;
        }
        ref readonly var surface = ref _surfaces[material];
        var materialAtPoint = surface;
        if (surface.RoughnessMap is not null || surface.MetallicMap is not null)
        {
            float u, v, footprint;
            if (surface.UvMapped && _scene.TriangleUvs is { } mapUvs)
            {
                var i = triangle * 3;
                var uv = mapUvs[i] * (1 - hit.U - hit.V) + mapUvs[i + 1] * hit.U + mapUvs[i + 2] * hit.V;
                var area2 = Vector3.Cross(_scene.Positions[_scene.Indices[i + 1]] - _scene.Positions[_scene.Indices[i]],
                    _scene.Positions[_scene.Indices[i + 2]] - _scene.Positions[_scene.Indices[i]]).Length();
                var uv1 = mapUvs[i + 1] - mapUvs[i];
                var uv2 = mapUvs[i + 2] - mapUvs[i];
                var density = area2 > 1e-12f ? MathF.Sqrt(MathF.Abs(uv1.X * uv2.Y - uv2.X * uv1.Y) / area2) : 1;
                u = uv.X;
                v = 1 - uv.Y;
                footprint = MathF.Max(hit.Distance * PixelAngle * density, 1e-5f);
            }
            else
            {
                (u, v) = SurfacePatterns.PlaneUv(position, geometric);
                u *= surface.TextureScale;
                v *= surface.TextureScale;
                footprint = MathF.Max(hit.Distance * PixelAngle * surface.TextureScale, 1e-5f);
            }
            var roughness = surface.RoughnessMap?.Sample(u, v, footprint) ?? surface.Roughness;
            var metallic = surface.MetallicMap?.Sample(u, v, footprint) ?? surface.Metallic;
            materialAtPoint = surface.WithPbr(roughness, metallic);
        }
        var diffuse = materialAtPoint.Diffuse;
        // Pattern detail only shows where the surface is seen sharply; after a diffuse bounce the light is blurred
        // anyway, so the pattern's mean value is used and the noise function is skipped (about 40 % of render time).
        if (surface.Texture is { } mapped && surface.UvMapped && _scene.TriangleUvs is { } uvs)
        {
            // A detailed family's own image, read with the mesh's coordinates; after a diffuse bounce only its mean.
            if (detailed)
            {
                var i = triangle * 3;
                var uv = uvs[i] * (1 - hit.U - hit.V) + uvs[i + 1] * hit.U + uvs[i + 2] * hit.V;
                // The detail level follows how much of the image a metre of this triangle covers: a trunk whose image
                // wraps several metres needs a sharper level than a leaf card of a few centimetres.
                var world = Vector3.Cross(_scene.Positions[_scene.Indices[i + 1]] - _scene.Positions[_scene.Indices[i]],
                    _scene.Positions[_scene.Indices[i + 2]] - _scene.Positions[_scene.Indices[i]]).Length();
                var (du1, du2) = (uvs[i + 1] - uvs[i], uvs[i + 2] - uvs[i]);
                var imagePerMetre = world > 1e-12f ? MathF.Sqrt(MathF.Abs(du1.X * du2.Y - du2.X * du1.Y) / world) : 1;
                var footprint = MathF.Max(hit.Distance * PixelAngle, 1e-5f) * imagePerMetre;
                diffuse *= mapped.Sample(uv.X, 1 - uv.Y, footprint);
                if (surface.NormalMap is { } relief)
                {
                    static Vector2 Up(Vector2 corner) => new(corner.X, 1 - corner.Y);
                    var bent = NormalMap.Perturb(normal, relief.Sample(uv.X, 1 - uv.Y, footprint), _scene.Positions[_scene.Indices[i]],
                        _scene.Positions[_scene.Indices[i + 1]], _scene.Positions[_scene.Indices[i + 2]], Up(uvs[i]), Up(uvs[i + 1]), Up(uvs[i + 2]));
                    // A bend that would turn the surface away from the eye is dropped, as for image relief.
                    if (Vector3.Dot(bent, direction) < -0.05f) normal = bent;
                }
                else if (surface.Pattern == SurfacePattern.Bark)
                {
                    // Textured bark without a supplied normal map still needs physical-looking furrows.
                    var bent = SurfacePatterns.Relief(SurfacePattern.Bark, 1, position, normal, hit.Distance);
                    if (Vector3.Dot(bent, direction) < -0.05f) normal = bent;
                }
                // A real grayscale height map is not inferred from albedo. UV meshes also receive
                // bump shading, so imported stone, timber and tiles can show fine depth at close range.
                if (surface.HeightMap is { } height && surface.TextureDepth > 0)
                {
                    static Vector2 UpHeight(Vector2 point) => new(point.X, 1 - point.Y);
                    var v = 1 - uv.Y;
                    var step = height.TexelSize(footprint);
                    var dU = (height.Sample(uv.X + step, v, footprint) -
                              height.Sample(uv.X - step, v, footprint)) / (2 * step);
                    var dV = (height.Sample(uv.X, v + step, footprint) -
                              height.Sample(uv.X, v - step, footprint)) / (2 * step);
                    var tangentNormal = Vector3.Normalize(new Vector3(-dU * surface.TextureDepth * imagePerMetre,
                        -dV * surface.TextureDepth * imagePerMetre, 1));
                    var bent = NormalMap.Perturb(normal, tangentNormal,
                        _scene.Positions[_scene.Indices[i]], _scene.Positions[_scene.Indices[i + 1]],
                        _scene.Positions[_scene.Indices[i + 2]], UpHeight(uvs[i]), UpHeight(uvs[i + 1]), UpHeight(uvs[i + 2]));
                    if (Vector3.Dot(bent, direction) < -0.05f) normal = bent;
                }
            }
            else
            {
                diffuse *= mapped.Mean;
            }
        }
        else if (surface.Texture is { } texture)
        {
            if (detailed)
            {
                // Mip level from the size of a pixel on the surface: one pixel at this distance, in tiles.
                var (u, v) = SurfacePatterns.PlaneUv(position, geometric);
                var tiles = surface.TextureScale;
                var footprint = MathF.Max(hit.Distance * PixelAngle, 1e-5f) * tiles;
                diffuse *= texture.Sample(u * tiles, v * tiles, footprint);
                if (surface.TextureDepth > 0)
                {
                    var height = surface.HeightMap;
                    var step = height?.TexelSize(footprint) ?? texture.TexelSize(footprint);
                    float SampleHeight(float x, float y) => height?.Sample(x, y, footprint) ?? texture.Luminance(x, y, footprint);
                    var dU = (SampleHeight(u * tiles + step, v * tiles) -
                              SampleHeight(u * tiles - step, v * tiles)) / (2 * step) * tiles;
                    var dV = (SampleHeight(u * tiles, v * tiles + step) -
                              SampleHeight(u * tiles, v * tiles - step)) / (2 * step) * tiles;
                    var bent = SurfacePatterns.Bend(normal, dU * surface.TextureDepth, dV * surface.TextureDepth);
                    if (Vector3.Dot(bent, direction) < -0.05f) normal = bent;
                }
            }
            else
            {
                diffuse *= texture.Mean;
            }
        }
        else if (surface.Pattern != SurfacePattern.None)
        {
            if (detailed)
            {
                diffuse *= SurfacePatterns.Modulate(surface.Pattern, surface.PatternScale, position, geometric);
                // Relief only where the surface is seen sharply, like the pattern itself. A bent normal that would
                // turn away from the viewer is dropped, so grazing views never show black facets.
                var bent = SurfacePatterns.Relief(surface.Pattern, surface.PatternScale, position, normal, hit.Distance);
                if (Vector3.Dot(bent, direction) < -0.05f) normal = bent;
                // Lawn: blades of grass (when the grass is on) replace the flat colour of the pattern.
                if (_grassy is not null && _grassy[material] && geometric.Z > 0.5f)
                    ApplyGrass(surface.PatternScale, position, geometric, direction, hit.Distance, ref diffuse, ref normal);
            }
            else
            {
                diffuse *= new Vector3(surface.PatternMean);
            }
        }
        if (detailed && !surface.UvMapped && surface.NormalMap is { } worldNormal)
        {
            var (u, v) = SurfacePatterns.PlaneUv(position, geometric);
            var footprint = MathF.Max(hit.Distance * PixelAngle * surface.TextureScale, 1e-5f);
            var sample = worldNormal.Sample(u * surface.TextureScale, v * surface.TextureScale, footprint);
            var bent = SurfacePatterns.MapNormal(normal, sample);
            if (Vector3.Dot(bent, direction) < -0.05f) normal = bent;
        }
        // Rain: level ground under open sky is wet (darker, glossier, with puddles and rings).
        if (_damp is not null && Wet(material, position, geometric, detailed, hit.Distance, ref diffuse, ref normal, direction) is { } wetSurface)
            materialAtPoint = wetSurface;
        return new ShadePoint(position, geometric, normal, diffuse, material, materialAtPoint);
    }

    /// <summary>
    /// Follows one camera path, adding direct light at every scattering vertex. Light gathered before the
    /// first diffuse bounce is returned as Direct; everything after it as Indirect.
    /// </summary>
    private (Vector3 Direct, Vector3 Indirect) Trace(Vector3 origin, Vector3 direction, ref Sampler sampler)
    {
        var direct = Vector3.Zero;
        var indirect = Vector3.Zero;
        var throughput = Vector3.One;
        var previousDelta = true;          // the last scatter was a perfect mirror or pass-through, so light hits count in full
        var previousPdf = 0f;
        var scatterPosition = Vector3.Zero; // where the last non-mirror scatter happened, for portal MIS
        var scatterNormal = Vector3.UnitZ;
        var sinceScatter = 0f;             // distance travelled since then (a straight path through glass keeps counting)
        var denoisable = true;             // no glossy reflection yet, so the indirect/direct split is meaningful
        var inIndirect = false;
        var bounce = 0;
        var crossings = 0;
        var clamp = _settings.FireflyClamp;

        // With a radiance cache the path keeps a record of its scatter vertices so that, once it ends, the light each
        // vertex sent back can be estimated and stored (L_i = D_i + w_i * L_{i+1}, evaluated from the last vertex down).
        var cache = _radianceCache;
        Span<PathVertex> vertices = cache is null ? default : stackalloc PathVertex[_settings.MaxBounces + 3];
        var vertexCount = 0;
        var current = -1;
        var recorded = Vector3.Zero;   // light gathered at the current vertex, written into its record when the path moves on

        while (bounce <= _settings.MaxBounces && crossings < _settings.MaxBounces + MaxGlassCrossings)
        {
            if (!Closest(origin, direction, float.MaxValue, out var hit, out var bvh))
            {
                // A diffuse or glossy path that leaves through a window could also have been generated by sampling
                // the window; weight the two strategies against each other so the sky is counted exactly once.
                var skyWeight = 1f;
                if (!previousDelta && _portals is not null)
                {
                    var portalPdf = _portals.Pdf(scatterPosition, scatterNormal, direction);
                    if (portalPdf > 0) skyWeight = Sampling.PowerHeuristic(previousPdf, portalPdf);
                }
                var radiance = SkyRadiance(direction) * skyWeight;
                var alignment = Vector3.Dot(direction, _sunDirection);
                if (alignment > _sunCosine && _sunRadiance != Vector3.Zero)
                {
                    var weight = previousDelta ? 1f : Sampling.PowerHeuristic(previousPdf, _sunPdf);
                    var visible = Vector3.Min(_sunRadiance, new Vector3(CameraSunRadianceLimit));
                    // Clouds can obscure the disk, instead of the former artificial clear circle around it.
                    radiance += visible * weight *
                        (_clouds?.SunTransmittance(origin, direction) ?? 1f);
                }
                if (cache is not null && vertexCount > 0)
                {
                    vertices[current].Direct += recorded;
                    recorded = default;
                    vertices[vertexCount++] = default;   // the sky as the final, non-scattering record of the path
                    current = vertexCount - 1;
                }
                Deposit(radiance);
                break;
            }

            sinceScatter += hit.Distance;
            var position = origin + direction * hit.Distance;
            if (bounce == 0 && _hazeDensity > 0)
            {
                // Aerial perspective on the way to the camera: the air in front of a distant surface veils it with
                // the color of the horizon in that direction.
                var transmittance = MathF.Exp(-hit.Distance * _hazeDensity);
                if (transmittance < 0.9999f)
                {
                    var horizon = Vector3.Normalize(new Vector3(direction.X, direction.Y, 0.04f));
                    Deposit(SkyRadiance(horizon) * (1 - transmittance));
                    throughput *= transmittance;
                }
            }
            var point = Shade(hit, bvh, position, direction, detailed: previousDelta);
            var surface = point.Surface;

            // Thin glass or a perforated film: a share of rays pass through, optionally reflecting at grazing angles.
            if (surface.Transmission > 0 && (surface.Transmission >= 1 || sampler.NextFloat() < surface.Transmission))
            {
                var cosine = MathF.Abs(Vector3.Dot(direction, point.Geometric));
                var reflectance = Microfacet.Fresnel(surface.DielectricF0, cosine);
                if (sampler.NextFloat() < reflectance)
                {
                    direction = Vector3.Reflect(direction, point.Geometric);
                    origin = position + point.Geometric * _rayEpsilon;
                    // Only a few samples of a pane reflect (Fresnel is a few percent face on), so what they see would
                    // stay as grain on every window. Seen from the camera it is filtered with the indirect light;
                    // the pane is flat, so the filter keeps the reflection's shape.
                    if (denoisable && bounce == 0) inIndirect = true;
                    else denoisable = false;
                    previousDelta = true;
                    sinceScatter = 0;
                }
                else
                {
                    // A straight pass keeps the light-sampling state, so what lies beyond is still weighted
                    // against the sampling of the sun, portals and emitters done at the last real scatter.
                    throughput *= surface.Tint;
                    if (current >= 0) vertices[current].Weight *= surface.Tint;
                    origin = position - point.Geometric * _rayEpsilon;
                }
                crossings++;
                continue;
            }

            // A cached estimate replaces everything the path would still do from here, but only where it is safe:
            // after a non-mirror bounce (the first visible surface is always computed exactly), on diffuse surfaces
            // that do not emit, so no light-source sampling is skipped or counted twice.
            var cacheable = cache is not null && surface.CacheEligible;
            if (cacheable && bounce >= 1 && !previousDelta)
            {
                _threadCacheLookups++;
                if (cache!.TryGet(point.Position, point.Normal, out var cached))
                {
                    _threadCacheHits++;
                    if (current >= 0) vertices[current].Direct += recorded;
                    recorded = default;
                    vertices[vertexCount++] = default;
                    current = vertexCount - 1;
                    Deposit(cached);
                    break;
                }
            }
            if (cache is not null)
            {
                if (current >= 0) vertices[current].Direct += recorded;
                recorded = default;
                vertices[vertexCount++] = new PathVertex { Position = point.Position, Normal = point.Normal, Eligible = cacheable };
                current = vertexCount - 1;
            }

            if (surface.IsEmissive)
            {
                var weight = 1f;
                if (!previousDelta && _emitters is not null)
                {
                    var pdfLight = _emitters.SolidAnglePdf(hit.Triangle, sinceScatter,
                        MathF.Abs(Vector3.Dot(point.Geometric, direction)));
                    weight = Sampling.PowerHeuristic(previousPdf, pdfLight);
                }
                Deposit(surface.Emission * weight);
            }

            var viewDirection = -direction;
            var shadowOrigin = position + point.Geometric * _rayEpsilon;
            if (surface.HasLobes)
                Deposit(DirectLight(in point, in surface, viewDirection, shadowOrigin, ref sampler));

            if (bounce == _settings.MaxBounces) break;

            if (!SampleBsdf(in point, in surface, viewDirection, ref sampler, out var next, out var weightBsdf,
                    out var pdf, out var delta, out var diffuseLobe, out var transmitted))
                break;
            // Russian roulette keeps cost bounded without bias once the path is dim.
            var continuation = weightBsdf;
            if (bounce >= 2)
            {
                var survival = MathF.Min(0.95f, MathF.Max(throughput.X, MathF.Max(throughput.Y, throughput.Z)));
                if (sampler.NextFloat() > survival) break;
                throughput /= survival;
                continuation /= survival;
            }
            throughput *= weightBsdf;
            if (current >= 0) vertices[current].Weight = continuation;
            // A rough glossy reflection (polished tile, satin paint) is as blurred as diffuse light, so it is denoised
            // with it; otherwise its noise stays as white grain on floors. Sharp reflections keep their detail.
            var blurred = diffuseLobe || (!delta && surface.Alpha >= RoughReflectionAlpha);
            if (blurred && denoisable) inIndirect = true;
            else if (!blurred) denoisable = false;
            previousDelta = delta;
            previousPdf = pdf;
            scatterPosition = position;
            scatterNormal = point.Normal;
            sinceScatter = 0;
            direction = next;
            // Light that passed through a translucent leaf continues from its far side.
            origin = transmitted ? position - point.Geometric * _rayEpsilon : shadowOrigin;
            bounce++;
        }

        if (cache is not null && vertexCount > 0)
        {
            vertices[current].Direct += recorded;
            // Walk back from the end of the path: what each vertex sent toward the camera is what it gathered itself
            // plus the (weighted) light that arrived from the next one.
            var arriving = Vector3.Zero;
            for (var i = vertexCount - 1; i >= 0; i--)
            {
                ref var vertex = ref vertices[i];
                arriving = vertex.Direct + vertex.Weight * arriving;
                if (vertex.Eligible)
                {
                    var luminance = Sampling.Luminance(arriving);
                    cache.Add(vertex.Position, vertex.Normal, luminance > CacheValueLimit ? arriving * (CacheValueLimit / luminance) : arriving);
                }
            }
        }
        return (direct, indirect);

        void Deposit(Vector3 gathered)
        {
            // Light that arrives after a scatter is the usual source of white speckles. Diffuse-chain light is limited
            // tightly, light after glossy reflections a little more loosely so mirror-like highlights survive.
            var contribution = throughput * gathered;
            var scale = 1f;
            if (clamp > 0 && (inIndirect || bounce > 0 || !denoisable))
            {
                var limit = inIndirect ? clamp : clamp * 4;
                var luminance = Sampling.Luminance(contribution);
                if (luminance > limit) scale = limit / luminance;
            }
            contribution *= scale;
            if (inIndirect) indirect += contribution; else direct += contribution;
            recorded += gathered * scale;
        }
    }

    /// <summary>Radiance of the sky dome (no sun disk) in a direction.</summary>
    private Vector3 SkyRadiance(Vector3 direction)
    {
        if (_settings.Environment is { } environment)
            return environment.Sample(direction, _settings.EnvironmentYawDegrees) * _skyScale;
        var sky = _sky.Radiance(direction) * _skyScale;
        if (_clouds is null || direction.Z <= 0.015f) return sky;
        var (opacity, thickness) = _clouds.Sample(direction);
        // The sun disk is attenuated separately in Trace(), allowing clouds to pass in front of it.
        if (opacity <= 0.001f) return sky;
        var cloud = _clouds.ShadeFor(direction, thickness, _sunDirection, _cloudSunlight, _cloudSkylight);
        return Vector3.Lerp(sky, cloud, opacity);
    }

    /// <summary>
    /// Samples the sun, explicit lights and emissive triangles at a vertex and returns the reflected radiance,
    /// weighted against BSDF sampling with multiple importance sampling.
    /// </summary>
    private Vector3 DirectLight(in ShadePoint point, in SurfaceData surface, Vector3 view, Vector3 origin,
        ref Sampler sampler)
    {
        var total = Vector3.Zero;
        var nDotV = MathF.Max(Vector3.Dot(point.Normal, view), 1e-4f);

        if (_sunRadiance != Vector3.Zero)
        {
            var light = Sampling.Cone(_sunDirection, _sunCosine, sampler.NextFloat(), sampler.NextFloat());
            if (Receives(in point, in surface, light, out var nDotL, out var cosine, out var from))
            {
                var transmittance = Visibility(from, light, float.MaxValue);
                if (transmittance != Vector3.Zero)
                {
                    var f = Evaluate(in surface, point.Diffuse, point.Normal, view, light, nDotV, nDotL, out var pdf);
                    // Keep sunlit roofs and ground consistent with the visible cloud layer.
                    var cloudTransmission = _clouds?.SunTransmittance(point.Position, light) ?? 1f;
                    total += transmittance * _sunRadiance * f *
                             (cosine / _sunPdf * Sampling.PowerHeuristic(_sunPdf, pdf) * cloudTransmission);
                }
            }
        }

        if (_lightSampler is { } lights)
        {
            var near = lights.All(point.Position, out var exhaustive);
            if (exhaustive)
            {
                foreach (var index in near)
                    total += LightContribution(lights, index, in point, in surface, view, origin, nDotV, ref sampler);
            }
            else if (lights.Resample(origin, point.Normal, point.Geometric, Sampling.Luminance(point.Diffuse), ref sampler,
                         out var chosen, out var risWeight))
            {
                total += LightContribution(lights, chosen, in point, in surface, view, origin, nDotV, ref sampler) * risWeight;
            }
        }

        if (_emitters is not null) total += _emitters.Sample(this, in point, in surface, view, origin, nDotV, ref sampler);

        if (_portals is not null && _portals.TrySample(point.Position, point.Normal, sampler.NextFloat(),
                sampler.NextFloat(), sampler.NextFloat(), out var portalDirection, out var portalPdf))
        {
            if (Receives(in point, in surface, portalDirection, out var nDotL, out var cosine, out var from))
            {
                var transmittance = Visibility(from, portalDirection, float.MaxValue);
                if (transmittance != Vector3.Zero)
                {
                    var f = Evaluate(in surface, point.Diffuse, point.Normal, view, portalDirection, nDotV, nDotL, out var pdf);
                    total += transmittance * SkyRadiance(portalDirection) * f *
                             (cosine / portalPdf * Sampling.PowerHeuristic(portalPdf, pdf));
                }
            }
        }
        return total;
    }

    /// <summary>
    /// Whether light arriving from <paramref name="direction"/> reaches the surface's lobes: from the front, or from
    /// behind through a translucent surface. Gives the signed and absolute cosines and the shadow-ray origin on that side.
    /// </summary>
    private bool Receives(in ShadePoint point, in SurfaceData surface, Vector3 direction, out float nDotL, out float cosine,
        out Vector3 origin)
    {
        nDotL = Vector3.Dot(point.Normal, direction);
        var geometric = Vector3.Dot(point.Geometric, direction);
        if (nDotL > 0 && geometric > 0)
        {
            cosine = nDotL;
            origin = point.Position + point.Geometric * _rayEpsilon;
            return true;
        }
        if (surface.Translucency > 0 && nDotL < 0 && geometric < 0)
        {
            cosine = -nDotL;
            origin = point.Position - point.Geometric * _rayEpsilon;
            return true;
        }
        cosine = 0;
        origin = default;
        return false;
    }

    /// <summary>Light reflected toward the viewer from one explicit light, with a shadow ray.</summary>
    private Vector3 LightContribution(LightSampler lights, int index, in ShadePoint point, in SurfaceData surface,
        Vector3 view, Vector3 origin, float nDotV, ref Sampler sampler)
    {
        var light = lights[index];
        var target = light.Position;
        if (light.Radius > 0)
        {
            // A point on the lamp's sphere gives soft shadows without changing the light's total power.
            target += Sampling.Cone(Vector3.UnitZ, -1f, sampler.NextFloat(), sampler.NextFloat()) * light.Radius;
        }
        var toLight = target - point.Position;
        var distanceSquared = MathF.Max(toLight.LengthSquared(), 1e-4f);
        var distance = MathF.Sqrt(distanceSquared);
        var direction = toLight / distance;
        if (!Receives(in point, in surface, direction, out var nDotL, out var cosine, out var from)) return Vector3.Zero;
        var intensity = lights.Intensity(index, -direction);
        if (intensity == Vector3.Zero) return Vector3.Zero;
        var transmittance = Visibility(from, direction, distance * 0.999f);
        if (transmittance == Vector3.Zero) return Vector3.Zero;
        var f = Evaluate(in surface, point.Diffuse, point.Normal, view, direction, nDotV, nDotL, out _);
        return transmittance * intensity * f * (cosine / distanceSquared);
    }

    // ---------------------------------------------------------------- BSDF

    /// <summary>
    /// Evaluates the surface response for incoming direction <paramref name="light"/> and returns the BSDF value
    /// (without the cosine term) plus the density with which <see cref="SampleBsdf"/> would pick that direction.
    /// </summary>
    private static Vector3 Evaluate(in SurfaceData surface, Vector3 diffuse, Vector3 normal, Vector3 view,
        Vector3 light, float nDotV, float nDotL, out float pdf)
    {
        var specularProbability = SpecularProbability(in surface, diffuse, nDotV);
        if (nDotL < 0)
        {
            // Diffuse transmission through a thin translucent surface: light from behind, scattered toward the viewer.
            pdf = (1 - specularProbability) * surface.Translucency * -nDotL * (1 / MathF.PI);
            return diffuse * (surface.Translucency * (1 / MathF.PI));
        }
        var half = Vector3.Normalize(light + view);
        var vDotH = MathF.Max(Vector3.Dot(view, half), 0);
        var fresnel = Microfacet.Fresnel(surface.F0, vDotH);
        var value = diffuse * ((Vector3.One - fresnel) * (1 / MathF.PI));
        pdf = (1 - specularProbability) * (1 - surface.Translucency) * nDotL * (1 / MathF.PI);
        if (surface.HasSpecular && !surface.Delta)
        {
            var nDotH = MathF.Max(Vector3.Dot(normal, half), 0);
            var alpha = surface.Alpha;
            value += fresnel * (Microfacet.Distribution(alpha, nDotH) * Microfacet.Visibility(alpha, nDotL, nDotV));
            pdf += specularProbability * Microfacet.Pdf(alpha, nDotV, nDotH, vDotH);
        }
        return value;
    }

    /// <summary>Chance of sampling the specular lobe: its Fresnel weight relative to the diffuse weight.</summary>
    private static float SpecularProbability(in SurfaceData surface, Vector3 diffuse, float nDotV)
    {
        if (!surface.HasSpecular) return 0;
        var specular = Sampling.Luminance(Microfacet.Fresnel(surface.F0, nDotV));
        var body = Sampling.Luminance(diffuse) * (1 - specular);
        if (body < 1e-4f) return 1;
        return Math.Clamp(specular / (specular + body), 0.06f, 0.94f);
    }

    /// <summary>
    /// Chooses the next direction. Returns the throughput weight (BSDF · cosine / pdf), the pdf for MIS (0 for
    /// perfect mirrors), and whether the lobe was diffuse.
    /// </summary>
    private bool SampleBsdf(in ShadePoint point, in SurfaceData surface, Vector3 view, ref Sampler sampler,
        out Vector3 next, out Vector3 weight, out float pdf, out bool delta, out bool diffuseLobe, out bool transmitted)
    {
        next = Vector3.UnitZ;
        weight = Vector3.Zero;
        pdf = 0;
        delta = false;
        diffuseLobe = false;
        transmitted = false;
        var normal = point.Normal;
        var nDotV = MathF.Max(Vector3.Dot(normal, view), 1e-4f);
        var specularProbability = SpecularProbability(in surface, point.Diffuse, nDotV);
        var chooseSpecular = specularProbability > 0 && sampler.NextFloat() < specularProbability;

        if (!chooseSpecular)
        {
            // A translucent surface sends a share of its diffuse light out of its back face.
            transmitted = surface.Translucency > 0 && sampler.NextFloat() < surface.Translucency;
            next = Sampling.CosineHemisphere(transmitted ? -normal : normal, sampler.NextFloat(), sampler.NextFloat());
            var nDotL = Vector3.Dot(normal, next);
            var geometric = Vector3.Dot(point.Geometric, next);
            if (transmitted ? nDotL >= -1e-5f || geometric >= 0 : nDotL <= 1e-5f || geometric <= 0) return false;
            var f = Evaluate(in surface, point.Diffuse, normal, view, next, nDotV, nDotL, out pdf);
            if (pdf <= 1e-9f) return false;
            weight = f * (MathF.Abs(nDotL) / pdf);
            diffuseLobe = true;
            return true;
        }

        if (surface.Delta)
        {
            next = Vector3.Reflect(-view, normal);
            if (Vector3.Dot(point.Geometric, next) <= 0) return false;
            weight = Microfacet.Fresnel(surface.F0, nDotV) / specularProbability;
            delta = true;
            return true;
        }

        var (tangent, bitangent) = Sampling.Basis(normal);
        var localView = new Vector3(Vector3.Dot(view, tangent), Vector3.Dot(view, bitangent), nDotV);
        var localHalf = Microfacet.SampleVisibleNormal(localView, surface.Alpha, sampler.NextFloat(), sampler.NextFloat());
        var half = tangent * localHalf.X + bitangent * localHalf.Y + normal * localHalf.Z;
        next = Vector3.Reflect(-view, half);
        var cosine = Vector3.Dot(normal, next);
        if (cosine <= 1e-5f || Vector3.Dot(point.Geometric, next) <= 0) return false;
        var value = Evaluate(in surface, point.Diffuse, normal, view, next, nDotV, cosine, out pdf);
        if (pdf <= 1e-9f) return false;
        weight = value * (cosine / pdf);
        return true;
    }

    // ---------------------------------------------------------------- per-material data

    /// <summary>One scatter vertex of a path, kept to estimate and cache the light it sent back.</summary>
    private struct PathVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        /// <summary>Light gathered here (emission, direct light, sky) per unit of path throughput.</summary>
        public Vector3 Direct;
        /// <summary>Factor applied to light arriving from the next vertex; zero when the path ended here.</summary>
        public Vector3 Weight;
        public bool Eligible;
    }

    /// <summary>Render-ready form of a <see cref="RenderMaterial"/>.</summary>
    private readonly record struct SurfaceData
    {
        public Vector3 BaseColor { get; init; }
        public float Metallic { get; init; }
        public float Roughness { get; init; }
        public RenderScalarMap? RoughnessMap { get; init; }
        public RenderScalarMap? MetallicMap { get; init; }
        public RenderScalarMap? HeightMap { get; init; }
        public Vector3 Diffuse { get; init; }
        public Vector3 F0 { get; init; }
        public Vector3 Emission { get; init; }
        public Vector3 Tint { get; init; }
        public float Alpha { get; init; }
        public float Transmission { get; init; }
        public float DielectricF0 { get; init; }
        public bool Delta { get; init; }
        public bool HasSpecular { get; init; }
        /// <summary>Diffuse enough that one cached value describes it from any side: not a mirror, metal, glass or light source.</summary>
        public bool CacheEligible { get; init; }
        public SurfacePattern Pattern { get; init; }
        public float PatternScale { get; init; }
        /// <summary>Average of the pattern's diffuse multiplier, used where its detail would be invisible.</summary>
        public float PatternMean { get; init; }
        public RenderTexture? Texture { get; init; }
        /// <summary>The texture is read with the mesh's coordinates rather than projected in world space.</summary>
        public bool UvMapped { get; init; }
        /// <summary>Relief of a coordinate-mapped surface, or null.</summary>
        public NormalMap? NormalMap { get; init; }
        /// <summary>Tiles per meter: the reciprocal of the texture size.</summary>
        public float TextureScale { get; init; }
        /// <summary>Relief height in meters of a full white-to-black step in the texture.</summary>
        public float TextureDepth { get; init; }
        /// <summary>Share of diffuse light passed through to the back face (leaves).</summary>
        public float Translucency { get; init; }
        public bool IsEmissive => Emission.X > 0 || Emission.Y > 0 || Emission.Z > 0;
        /// <summary>Whether direct light can be evaluated here (anything except a pure mirror or pure emitter).</summary>
        public bool HasLobes { get; init; }

        /// <summary>Recomputes the local GGX/Fresnel lobes from map values, without altering shared material state.</summary>
        public SurfaceData WithPbr(float roughness, float metallic)
        {
            roughness = float.IsFinite(roughness) ? Math.Clamp(roughness, 0.02f, 1f) : Roughness;
            metallic = float.IsFinite(metallic) ? Math.Clamp(metallic, 0f, 1f) : Metallic;
            var delta = roughness <= 0.05f;
            var hasSpecular = metallic > 0.01f || roughness < 0.85f;
            var diffuse = BaseColor * (1f - metallic);
            return this with
            {
                Roughness = roughness,
                Metallic = metallic,
                Diffuse = diffuse,
                F0 = Vector3.Lerp(new Vector3(DielectricF0), BaseColor, metallic),
                Alpha = MathF.Max(roughness * roughness, 0.0025f),
                Delta = delta,
                HasSpecular = hasSpecular,
                CacheEligible = !delta && metallic <= 0.05f && Transmission <= 0 &&
                    Translucency <= 0 && !IsEmissive,
                HasLobes = Sampling.Luminance(diffuse) > 1e-4f || (hasSpecular && !delta)
            };
        }

        public static SurfaceData From(RenderMaterial material)
        {
            var m = material.Sanitized();
            var f0 = Vector3.Lerp(new Vector3(m.DielectricF0), m.BaseColor, m.Metallic);
            var delta = m.Roughness <= 0.05f;
            var alpha = MathF.Max(m.Roughness * m.Roughness, 0.0025f);
            // Very rough dielectrics reflect almost nothing specularly; skipping the lobe saves noise and time.
            var hasSpecular = m.Metallic > 0.01f || m.Roughness < 0.85f;
            var diffuse = m.BaseColor * (1 - m.Metallic);
            return new SurfaceData
            {
                BaseColor = m.BaseColor,
                Metallic = m.Metallic,
                Roughness = m.Roughness,
                RoughnessMap = m.RoughnessMap,
                MetallicMap = m.MetallicMap,
                HeightMap = m.HeightMap,
                Diffuse = diffuse,
                F0 = f0,
                Emission = m.Emission,
                Tint = m.TransmissionTint,
                Alpha = alpha,
                Transmission = m.Transmission,
                DielectricF0 = m.DielectricF0,
                Delta = delta,
                HasSpecular = hasSpecular,
                CacheEligible = !delta && m.Metallic <= 0.05f && m.Transmission <= 0 && m.Translucency <= 0 &&
                                !(m.Emission.X > 0 || m.Emission.Y > 0 || m.Emission.Z > 0),
                Translucency = m.Translucency,
                HasLobes = Sampling.Luminance(diffuse) > 1e-4f || (hasSpecular && !delta),
                Pattern = m.Pattern,
                PatternScale = m.PatternScale,
                PatternMean = SurfacePatterns.Average(m.Pattern, m.PatternScale),
                Texture = m.Texture,
                UvMapped = m.UvMapped,
                NormalMap = m.NormalMap,
                TextureScale = 1 / m.TextureSize,
                // A full relief is a hundredth of the tile: about 1 cm of joint depth on a one-meter brick image.
                TextureDepth = m.TextureRelief * m.TextureSize * 0.01f
            };
        }
    }

    // ---------------------------------------------------------------- emissive triangles

    /// <summary>Importance-sampling table over emissive triangles (probability ∝ area · luminance).</summary>
    private sealed class Emitters
    {
        private readonly int[] _triangles;
        private readonly float[] _cumulative;
        private readonly float _total;
        private readonly Dictionary<int, int> _slotOf;
        private readonly float[] _areas;
        public int Count => _triangles.Length;
        private readonly Vector3[] _a;
        private readonly Vector3[] _b;
        private readonly Vector3[] _c;
        private readonly Vector3[] _normal;
        private readonly Vector3[] _emission;

        private Emitters(List<int> triangles, RenderScene scene, SurfaceData[] surfaces)
        {
            _triangles = triangles.ToArray();
            var count = _triangles.Length;
            _cumulative = new float[count];
            _areas = new float[count];
            _a = new Vector3[count];
            _b = new Vector3[count];
            _c = new Vector3[count];
            _normal = new Vector3[count];
            _emission = new Vector3[count];
            _slotOf = new Dictionary<int, int>(count);
            var running = 0f;
            for (var i = 0; i < count; i++)
            {
                var triangle = _triangles[i];
                _a[i] = scene.Positions[scene.Indices[triangle * 3]];
                _b[i] = scene.Positions[scene.Indices[triangle * 3 + 1]];
                _c[i] = scene.Positions[scene.Indices[triangle * 3 + 2]];
                var cross = Vector3.Cross(_b[i] - _a[i], _c[i] - _a[i]);
                _areas[i] = 0.5f * cross.Length();
                _normal[i] = cross / MathF.Max(cross.Length(), 1e-12f);
                _emission[i] = surfaces[scene.TriangleMaterial[triangle]].Emission;
                running += _areas[i] * MathF.Max(Sampling.Luminance(_emission[i]), 1e-4f);
                _cumulative[i] = running;
                _slotOf[triangle] = i;
            }
            _total = running;
        }

        public static Emitters? TryCreate(RenderScene scene, SurfaceData[] surfaces, Bvh opaque)
        {
            var triangles = new List<int>();
            for (var triangle = 0; triangle < scene.TriangleCount; triangle++)
                if (surfaces[scene.TriangleMaterial[triangle]].IsEmissive) triangles.Add(triangle);
            // A scene full of emissive triangles (for example a glowing imported mesh) would make sampling slow; cap it.
            return triangles.Count is > 0 and <= 200_000 ? new Emitters(triangles, scene, surfaces) : null;
        }

        /// <summary>Solid-angle density of choosing triangle <paramref name="triangle"/> from a point at the given distance.</summary>
        public float SolidAnglePdf(int triangle, float distance, float cosine)
        {
            if (!_slotOf.TryGetValue(triangle, out var slot) || cosine < 1e-5f) return 0;
            var probability = _areas[slot] * MathF.Max(Sampling.Luminance(_emission[slot]), 1e-4f) / _total;
            return probability * distance * distance / (cosine * _areas[slot]);
        }

        public Vector3 Sample(PathTracer tracer, in ShadePoint point, in SurfaceData surface, Vector3 view,
            Vector3 origin, float nDotV, ref Sampler sampler)
        {
            var pick = sampler.NextFloat() * _total;
            var slot = Array.BinarySearch(_cumulative, pick);
            if (slot < 0) slot = ~slot;
            slot = Math.Min(slot, _triangles.Length - 1);
            var target = Sampling.TrianglePoint(_a[slot], _b[slot], _c[slot], sampler.NextFloat(), sampler.NextFloat());
            var toLight = target - point.Position;
            var distanceSquared = toLight.LengthSquared();
            if (distanceSquared < 1e-6f) return Vector3.Zero;
            var distance = MathF.Sqrt(distanceSquared);
            var direction = toLight / distance;
            var lightCosine = MathF.Abs(Vector3.Dot(_normal[slot], direction));
            if (lightCosine < 1e-5f ||
                !tracer.Receives(in point, in surface, direction, out var nDotL, out var cosine, out var from)) return Vector3.Zero;
            var probability = _areas[slot] * MathF.Max(Sampling.Luminance(_emission[slot]), 1e-4f) / _total;
            var pdfLight = probability * distanceSquared / (lightCosine * _areas[slot]);
            var transmittance = tracer.Visibility(from, direction, distance * 0.998f - tracer._rayEpsilon);
            if (transmittance == Vector3.Zero) return Vector3.Zero;
            var f = Evaluate(in surface, point.Diffuse, point.Normal, view, direction, nDotV, nDotL, out var pdfBsdf);
            return transmittance * _emission[slot] * f * (cosine / pdfLight * Sampling.PowerHeuristic(pdfLight, pdfBsdf));
        }
    }
}
