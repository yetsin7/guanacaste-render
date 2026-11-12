// Navigation view: the scene rasterised in one pass, with no ray tracing. It reads the triangles, shading normals
// and materials straight from the path tracer's buffers (vertex pulling), so the fast view costs no extra copy of
// the scene. Lighting is the same sun and sky the path tracer uses, without shadows or bounced light, and the same
// exposure and tone curve, so switching to the preview changes the light, not the look.

struct View {
    position: vec3f,
    near: f32,
    right: vec3f,     // scaled by tan(fov / 2) * aspect, as in the path tracer
    exposure: f32,
    up: vec3f,        // scaled by tan(fov / 2)
    sky_intensity: f32,
    forward: vec3f,
    sun_intensity: f32,
    sun_direction: vec3f,
    sun_cos_radius: f32,
}

struct Triangle {
    v0: vec3f,
    material: u32,
    e1: vec3f,
    original: u32,
    e2: vec3f,
    pad: u32,
}

struct CornerNormals {
    n0: vec3f,
    pad0: f32,
    n1: vec3f,
    pad1: f32,
    n2: vec3f,
    pad2: f32,
}

struct Material {
    base_color: vec3f,
    roughness: f32,
    emission: vec3f,
    metallic: f32,
    transmission_tint: vec3f,
    transmission: f32,
    ior: f32,
    pad0: f32,
    pad1: f32,
    pad2: f32,
}

@group(0) @binding(0) var<uniform> view: View;
@group(0) @binding(1) var<storage, read> triangles: array<Triangle>;
@group(0) @binding(2) var<storage, read> materials: array<Material>;
// One entry per triangle, or a single inert entry when the whole scene is flat.
@group(0) @binding(3) var<storage, read> corner_normals: array<CornerNormals>;

const PI: f32 = 3.14159265;

struct Vertex {
    @builtin(position) clip: vec4f,
    @location(0) world: vec3f,
    @location(1) normal: vec3f,
    @location(2) @interpolate(flat) material: u32,
}

// Perspective with an infinite far plane and reversed depth: depth is near / distance, 1 at the near plane and
// falling towards 0, which keeps precision across a whole site.
fn project(point: vec3f) -> vec4f {
    let d = point - view.position;
    return vec4f(dot(d, view.right) / dot(view.right, view.right), dot(d, view.up) / dot(view.up, view.up), view.near, dot(d, view.forward));
}

@vertex
fn vs_main(@builtin(vertex_index) index: u32) -> Vertex {
    let slot = index / 3u;
    let corner = index % 3u;
    let triangle = triangles[slot];
    var position = triangle.v0;
    if corner == 1u { position += triangle.e1; }
    if corner == 2u { position += triangle.e2; }
    var normal = cross(triangle.e1, triangle.e2);
    if slot < arrayLength(&corner_normals) {
        let corners = corner_normals[slot];
        var smooth_normal = corners.n0;
        if corner == 1u { smooth_normal = corners.n1; }
        if corner == 2u { smooth_normal = corners.n2; }
        if dot(smooth_normal, smooth_normal) > 1e-8 { normal = smooth_normal; }
    }
    var out: Vertex;
    out.clip = project(position);
    out.world = position;
    out.normal = normal;
    out.material = triangle.material;
    return out;
}

fn sky(direction: vec3f) -> vec3f {
    let height = direction.z;
    let zenith = vec3f(0.19, 0.38, 0.86);
    let horizon = vec3f(0.74, 0.83, 0.95);
    var colour = mix(horizon, zenith, pow(clamp(height, 0.0, 1.0), 0.45));
    let towards_sun = max(dot(direction, view.sun_direction), 0.0);
    colour += vec3f(1.0, 0.82, 0.60) * 0.35 * pow(towards_sun, 24.0);
    if height < 0.0 {
        colour = mix(horizon, vec3f(0.22, 0.21, 0.20), clamp(-height * 6.0, 0.0, 1.0));
    }
    colour *= view.sky_intensity;
    if towards_sun > view.sun_cos_radius && view.sun_intensity > 0.0 {
        colour += vec3f(1.0, 0.96, 0.90) * view.sun_intensity * PI / (2.0 * PI * (1.0 - view.sun_cos_radius));
    }
    return colour;
}

fn filmic(x: vec3f) -> vec3f {
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), vec3f(0.0), vec3f(1.0));
}

fn srgb(linear: vec3f) -> vec3f {
    let low = linear * 12.92;
    let high = 1.055 * pow(linear, vec3f(1.0 / 2.4)) - 0.055;
    return select(high, low, linear <= vec3f(0.0031308));
}

// The picture as the window shows it: exposure, the path tracer's filmic curve, then sRGB encoding.
fn display(radiance: vec3f) -> vec3f {
    return srgb(filmic(max(radiance * view.exposure, vec3f(0.0))));
}

// The surface normal turned to face the viewer, so the soup of triangles needs no consistent winding.
fn facing_normal(input: Vertex) -> vec3f {
    let to_eye = normalize(view.position - input.world);
    var n = normalize(input.normal);
    if dot(n, to_eye) < 0.0 { n = -n; }
    return n;
}

// Sunlight without shadows, and the sky as a hemisphere: what the path tracer computes on its first bounce,
// minus visibility. Shiny surfaces add a sun highlight and a little of the sky they mirror.
fn lit(input: Vertex) -> vec3f {
    let material = materials[input.material];
    let n = facing_normal(input);
    let to_eye = normalize(view.position - input.world);
    let n_dot_l = max(dot(n, view.sun_direction), 0.0);
    let sun = vec3f(1.0, 0.96, 0.90) * view.sun_intensity * n_dot_l;
    let up = n.z * 0.5 + 0.5;
    let sky_light = view.sky_intensity * mix(vec3f(0.30, 0.29, 0.27), vec3f(0.52, 0.62, 0.80), up) * 0.85;
    let diffuse = material.base_color * (1.0 - material.metallic);
    var colour = diffuse * (sun + sky_light) + material.emission;
    let f0 = mix(vec3f(0.04), material.base_color, material.metallic);
    let fresnel = f0 + (vec3f(1.0) - f0) * pow(1.0 - max(dot(n, to_eye), 0.0), 5.0);
    let shininess = 2.0 / max(pow(material.roughness, 4.0), 1e-4);
    let half_vector = normalize(to_eye + view.sun_direction);
    let highlight = pow(max(dot(n, half_vector), 0.0), min(shininess, 4096.0)) * (shininess + 8.0) / (8.0 * PI);
    colour += fresnel * sun * highlight;
    colour += fresnel * sky(reflect(-to_eye, n)) * (1.0 - material.roughness);
    return colour;
}

@fragment
fn fs_opaque(input: Vertex) -> @location(0) vec4f {
    if materials[input.material].transmission > 0.001 { discard; }
    return vec4f(display(lit(input)), 1.0);
}

// Glass: what it reflects at a grazing angle, and a faint tint where one looks through it.
@fragment
fn fs_glass(input: Vertex) -> @location(0) vec4f {
    let material = materials[input.material];
    if material.transmission <= 0.001 { discard; }
    let n = facing_normal(input);
    let to_eye = normalize(view.position - input.world);
    let r0 = pow((material.ior - 1.0) / (material.ior + 1.0), 2.0);
    let reflectance = r0 + (1.0 - r0) * pow(1.0 - max(dot(n, to_eye), 0.0), 5.0);
    let reflected = sky(reflect(-to_eye, n));
    let tinted = material.transmission_tint * view.sky_intensity * 0.12;
    let alpha = clamp(0.18 + reflectance * 0.8, 0.0, 0.95);
    return vec4f(display(mix(tinted, reflected, reflectance / alpha * 0.9)), alpha);
}

struct SkyVertex {
    @builtin(position) clip: vec4f,
    @location(0) ndc: vec2f,
}

// One triangle that covers the screen, at the far end of the reversed depth range.
@vertex
fn vs_sky(@builtin(vertex_index) index: u32) -> SkyVertex {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u)) * 2.0 - 1.0;
    var out: SkyVertex;
    out.clip = vec4f(corner, 0.0, 1.0);
    out.ndc = corner;
    return out;
}

@fragment
fn fs_sky(input: SkyVertex) -> @location(0) vec4f {
    let direction = normalize(view.forward + view.right * input.ndc.x + view.up * input.ndc.y);
    return vec4f(display(sky(direction)), 1.0);
}
