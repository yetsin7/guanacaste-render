// Progressive path tracer in a compute shader. One invocation adds one sample to one pixel.
// The hierarchy is the depth-first binary tree of gr_core::bvh::FlatBvh: the left child of node i is node i + 1.
// World: Z up, metres. Radiance units follow the C# engine: sun_intensity is irradiance divided by pi.

struct Uniforms {
    camera_position: vec3f,
    frame: u32,
    camera_right: vec3f,   // scaled by tan(fov / 2) * aspect
    width: u32,
    camera_up: vec3f,      // scaled by tan(fov / 2)
    height: u32,
    camera_forward: vec3f,
    max_bounces: u32,
    sun_direction: vec3f,
    sun_intensity: f32,
    sky_intensity: f32,
    sun_cos_radius: f32,
    seed: u32,
    mode: u32,             // 0 = path tracing, 1 = primary hit distance (verification)
}

struct Node {
    box_min: vec3f,
    right_or_first: u32,
    box_max: vec3f,
    count: u32,
}

struct Triangle {
    v0: vec3f,
    material: u32,
    e1: vec3f,
    original: u32,
    e2: vec3f,
    pad: u32,
}

// Shading normals of the three corners of a triangle; all zero for a flat one.
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

@group(0) @binding(0) var<uniform> u: Uniforms;
@group(0) @binding(1) var<storage, read> nodes: array<Node>;
@group(0) @binding(2) var<storage, read> triangles: array<Triangle>;
@group(0) @binding(3) var<storage, read> materials: array<Material>;
@group(0) @binding(4) var<storage, read_write> accumulation: array<vec4f>;
@group(0) @binding(5) var<storage, read_write> ray_counter: array<atomic<u32>, 1>;
// One entry per triangle, in the order of `triangles`, or a single inert entry when the scene is all flat.
@group(0) @binding(6) var<storage, read> corner_normals: array<CornerNormals>;
// What the denoiser steers by: the shading normal and the distance of the first hit of each pixel.
@group(0) @binding(7) var<storage, read_write> features: array<vec4f>;

const PI: f32 = 3.14159265;
const NO_HIT: u32 = 0xffffffffu;
const FAR: f32 = 1e30;
const STACK_SIZE: u32 = 64u;

var<private> rng_state: u32;

fn pcg(value: u32) -> u32 {
    let state = value * 747796405u + 2891336453u;
    let word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

fn random() -> f32 {
    rng_state = pcg(rng_state);
    return f32(rng_state >> 8u) * (1.0 / 16777216.0);
}

struct Hit {
    distance: f32,
    triangle: u32,
    // Barycentric weights of the second and third corners at the hit.
    weights: vec2f,
}

fn safe_inverse(direction: vec3f) -> vec3f {
    let tiny = abs(direction) < vec3f(1e-20);
    return 1.0 / select(direction, vec3f(1e-20), tiny);
}

fn slab(box_min: vec3f, box_max: vec3f, origin: vec3f, inverse: vec3f, limit: f32) -> f32 {
    let t1 = (box_min - origin) * inverse;
    let t2 = (box_max - origin) * inverse;
    let near = min(t1, t2);
    let far = max(t1, t2);
    let entry = max(max(near.x, near.y), max(near.z, 0.0));
    let exit = min(min(far.x, far.y), min(far.z, limit));
    return select(FAR, entry, entry <= exit);
}

// Moller-Trumbore, two-sided. Returns the distance, or FAR when the triangle is missed.
fn triangle_distance(slot: u32, origin: vec3f, direction: vec3f, t_min: f32, t_max: f32) -> f32 {
    return triangle_hit(slot, origin, direction, t_min, t_max).x;
}

// As `triangle_distance`, with the barycentric weights of the hit in y and z.
fn triangle_hit(slot: u32, origin: vec3f, direction: vec3f, t_min: f32, t_max: f32) -> vec3f {
    let e1 = triangles[slot].e1;
    let e2 = triangles[slot].e2;
    let p = cross(direction, e2);
    let determinant = dot(e1, p);
    if abs(determinant) < 1e-12 { return vec3f(FAR, 0.0, 0.0); }
    let inverse = 1.0 / determinant;
    let t = origin - triangles[slot].v0;
    let bu = dot(t, p) * inverse;
    if bu < 0.0 || bu > 1.0 { return vec3f(FAR, 0.0, 0.0); }
    let q = cross(t, e1);
    let bv = dot(direction, q) * inverse;
    if bv < 0.0 || bu + bv > 1.0 { return vec3f(FAR, 0.0, 0.0); }
    let distance = dot(e2, q) * inverse;
    return vec3f(select(FAR, distance, distance > t_min && distance < t_max), bu, bv);
}

// The normal to shade a hit with: interpolated from the corners of a smooth triangle (a trunk, a branch), or
// the face normal `face`, which already looks towards the ray.
fn shading_normal(hit: Hit, face: vec3f) -> vec3f {
    if hit.triangle >= arrayLength(&corner_normals) { return face; }
    let corners = corner_normals[hit.triangle];
    let smooth_normal = corners.n0 * (1.0 - hit.weights.x - hit.weights.y) + corners.n1 * hit.weights.x + corners.n2 * hit.weights.y;
    // A flat triangle has zero normals at its corners.
    if dot(smooth_normal, smooth_normal) < 1e-8 { return face; }
    let unit = normalize(smooth_normal);
    return select(-unit, unit, dot(unit, face) >= 0.0);
}

fn closest_hit(origin: vec3f, direction: vec3f, t_min: f32) -> Hit {
    var hit = Hit(FAR, NO_HIT, vec2f(0.0));
    if arrayLength(&nodes) == 0u { return hit; }
    let inverse = safe_inverse(direction);
    var stack: array<u32, STACK_SIZE>;
    var top = 0u;
    var index = 0u;
    loop {
        let count = nodes[index].count;
        if count > 0u {
            let first = nodes[index].right_or_first;
            for (var slot = first; slot < first + count; slot++) {
                let candidate = triangle_hit(slot, origin, direction, t_min, hit.distance);
                if candidate.x < hit.distance {
                    hit.distance = candidate.x;
                    hit.weights = candidate.yz;
                    hit.triangle = slot;
                }
            }
        } else {
            var near = index + 1u;
            var far = nodes[index].right_or_first;
            var near_distance = slab(nodes[near].box_min, nodes[near].box_max, origin, inverse, hit.distance);
            var far_distance = slab(nodes[far].box_min, nodes[far].box_max, origin, inverse, hit.distance);
            if far_distance < near_distance {
                let swapped = near; near = far; far = swapped;
                let swapped_distance = near_distance; near_distance = far_distance; far_distance = swapped_distance;
            }
            if near_distance < FAR {
                if far_distance < FAR && top < STACK_SIZE {
                    stack[top] = far;
                    top++;
                }
                index = near;
                continue;
            }
        }
        if top == 0u { break; }
        top--;
        index = stack[top];
    }
    return hit;
}

// Light that reaches along a shadow segment: zero behind opaque geometry, tinted through glass.
fn transmittance(origin: vec3f, direction: vec3f, t_min: f32, t_max: f32) -> vec3f {
    var light = vec3f(1.0);
    if arrayLength(&nodes) == 0u { return light; }
    let inverse = safe_inverse(direction);
    var stack: array<u32, STACK_SIZE>;
    var top = 0u;
    var index = 0u;
    loop {
        let count = nodes[index].count;
        if count > 0u {
            let first = nodes[index].right_or_first;
            for (var slot = first; slot < first + count; slot++) {
                if triangle_distance(slot, origin, direction, t_min, t_max) < FAR {
                    let material = materials[triangles[slot].material];
                    if material.transmission <= 0.001 { return vec3f(0.0); }
                    light *= material.transmission_tint * material.transmission;
                }
            }
        } else {
            let left = index + 1u;
            let right = nodes[index].right_or_first;
            let hit_left = slab(nodes[left].box_min, nodes[left].box_max, origin, inverse, t_max) < FAR;
            let hit_right = slab(nodes[right].box_min, nodes[right].box_max, origin, inverse, t_max) < FAR;
            if hit_left {
                if hit_right && top < STACK_SIZE {
                    stack[top] = right;
                    top++;
                }
                index = left;
                continue;
            }
            if hit_right {
                index = right;
                continue;
            }
        }
        if top == 0u { break; }
        top--;
        index = stack[top];
    }
    return light;
}

// Analytic daylight: a horizon-to-zenith gradient, a warm band towards the sun and a ground colour below the horizon.
fn sky(direction: vec3f, with_sun_disk: bool) -> vec3f {
    let height = direction.z;
    let zenith = vec3f(0.19, 0.38, 0.86);
    let horizon = vec3f(0.74, 0.83, 0.95);
    var colour = mix(horizon, zenith, pow(clamp(height, 0.0, 1.0), 0.45));
    let towards_sun = max(dot(direction, u.sun_direction), 0.0);
    colour += vec3f(1.0, 0.82, 0.60) * 0.35 * pow(towards_sun, 24.0);
    if height < 0.0 {
        colour = mix(horizon, vec3f(0.22, 0.21, 0.20), clamp(-height * 6.0, 0.0, 1.0));
    }
    colour *= u.sky_intensity;
    if with_sun_disk && towards_sun > u.sun_cos_radius && u.sun_intensity > 0.0 {
        // Radiance of a disk that delivers irradiance pi * sun_intensity.
        colour += vec3f(1.0, 0.96, 0.90) * u.sun_intensity * PI / (2.0 * PI * (1.0 - u.sun_cos_radius));
    }
    return colour;
}

fn basis(normal: vec3f) -> mat3x3f {
    let helper = select(vec3f(1.0, 0.0, 0.0), vec3f(0.0, 0.0, 1.0), abs(normal.z) < 0.9);
    let tangent = normalize(cross(helper, normal));
    return mat3x3f(tangent, cross(normal, tangent), normal);
}

fn cosine_hemisphere(normal: vec3f) -> vec3f {
    let r = sqrt(random());
    let phi = 2.0 * PI * random();
    let z = sqrt(max(0.0, 1.0 - r * r));
    return normalize(basis(normal) * vec3f(r * cos(phi), r * sin(phi), z));
}

fn schlick(f0: vec3f, cosine: f32) -> vec3f {
    return f0 + (vec3f(1.0) - f0) * pow(clamp(1.0 - cosine, 0.0, 1.0), 5.0);
}

fn ggx_distribution(n_dot_h: f32, alpha: f32) -> f32 {
    let a2 = alpha * alpha;
    let d = n_dot_h * n_dot_h * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d);
}

fn smith_visibility(n_dot_v: f32, n_dot_l: f32, alpha: f32) -> f32 {
    // Height-correlated Smith visibility, already divided by 4 (n.v)(n.l).
    let a2 = alpha * alpha;
    let v = n_dot_l * sqrt(n_dot_v * n_dot_v * (1.0 - a2) + a2);
    let l = n_dot_v * sqrt(n_dot_l * n_dot_l * (1.0 - a2) + a2);
    return 0.5 / max(v + l, 1e-6);
}

fn luminance(colour: vec3f) -> f32 {
    return dot(colour, vec3f(0.2126, 0.7152, 0.0722));
}

fn sample_sun() -> vec3f {
    // Uniform direction inside the cone of the sun disk, for soft shadow edges.
    let cosine = mix(u.sun_cos_radius, 1.0, random());
    let sine = sqrt(max(0.0, 1.0 - cosine * cosine));
    let phi = 2.0 * PI * random();
    return normalize(basis(u.sun_direction) * vec3f(sine * cos(phi), sine * sin(phi), cosine));
}

fn primary_ray(pixel: vec2u, jitter: vec2f) -> vec3f {
    let x = (f32(pixel.x) + jitter.x) / f32(u.width) * 2.0 - 1.0;
    let y = 1.0 - (f32(pixel.y) + jitter.y) / f32(u.height) * 2.0;
    return normalize(u.camera_forward + u.camera_right * x + u.camera_up * y);
}

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) id: vec3u) {
    if id.x >= u.width || id.y >= u.height { return; }
    let pixel = id.y * u.width + id.x;

    if u.mode == 1u {
        let hit = closest_hit(u.camera_position, primary_ray(id.xy, vec2f(0.5)), 1e-4);
        accumulation[pixel] = vec4f(select(-1.0, hit.distance, hit.triangle != NO_HIT), 0.0, 0.0, 1.0);
        return;
    }

    rng_state = pcg(pixel ^ pcg(u.frame * 0x9e3779b9u + u.seed));
    var origin = u.camera_position;
    var direction = primary_ray(id.xy, vec2f(random(), random()));
    var throughput = vec3f(1.0);
    var radiance = vec3f(0.0);
    var rays = 0u;
    var sees_sun_disk = true;

    for (var bounce = 0u; bounce <= u.max_bounces; bounce++) {
        let hit = closest_hit(origin, direction, 1e-4);
        rays++;
        if hit.triangle == NO_HIT {
            if bounce == 0u && u.frame == 0u { features[pixel] = vec4f(0.0, 0.0, 0.0, -1.0); }
            radiance += throughput * sky(direction, sees_sun_disk);
            break;
        }
        let triangle = triangles[hit.triangle];
        let material = materials[triangle.material];
        // `face` is the true surface, used to step off it; `normal` is what the light sees.
        var face = normalize(cross(triangle.e1, triangle.e2));
        if dot(face, direction) > 0.0 { face = -face; }
        let normal = shading_normal(hit, face);
        if bounce == 0u && u.frame == 0u { features[pixel] = vec4f(normal, hit.distance); }
        let position = origin + direction * hit.distance;
        let view = -direction;
        let n_dot_v = max(dot(normal, view), 1e-4);
        radiance += throughput * material.emission;

        if material.transmission > 0.001 {
            // Thin architectural glass: a Fresnel reflection or a tinted straight pass, no bending.
            let r0 = pow((material.ior - 1.0) / (material.ior + 1.0), 2.0);
            let reflectance = r0 + (1.0 - r0) * pow(1.0 - n_dot_v, 5.0);
            if random() < reflectance {
                direction = reflect(direction, face);
                origin = position + face * 1e-3;
            } else {
                throughput *= material.transmission_tint;
                origin = position - face * 1e-3;
            }
            continue;
        }

        let alpha = max(material.roughness * material.roughness, 0.004);
        let f0 = mix(vec3f(0.04), material.base_color, material.metallic);
        let diffuse = material.base_color * (1.0 - material.metallic);
        let surface = position + face * 1e-3;

        // Direct sunlight with the full surface response.
        if u.sun_intensity > 0.0 {
            let to_sun = sample_sun();
            let n_dot_l = dot(normal, to_sun);
            if n_dot_l > 0.0 {
                rays++;
                let visible = transmittance(surface, to_sun, 1e-4, FAR);
                if visible.x + visible.y + visible.z > 0.0 {
                    let half_vector = normalize(view + to_sun);
                    let fresnel = schlick(f0, max(dot(view, half_vector), 0.0));
                    let specular = fresnel * ggx_distribution(max(dot(normal, half_vector), 0.0), alpha)
                        * smith_visibility(n_dot_v, n_dot_l, alpha);
                    let response = diffuse * (vec3f(1.0) - fresnel) / PI + specular;
                    radiance += throughput * visible * response * (u.sun_intensity * PI) * n_dot_l;
                }
            }
        }

        // Continue along the specular or the diffuse lobe, chosen by how much energy each carries.
        let fresnel_view = schlick(f0, n_dot_v);
        let specular_weight = luminance(fresnel_view);
        let diffuse_weight = luminance(diffuse * (vec3f(1.0) - fresnel_view));
        let specular_chance = clamp(specular_weight / max(specular_weight + diffuse_weight, 1e-6), 0.05, 0.95);
        if random() < specular_chance {
            // Sample the GGX distribution of half vectors.
            let xi = random();
            let cos_theta = sqrt((1.0 - xi) / (1.0 + (alpha * alpha - 1.0) * xi));
            let sin_theta = sqrt(max(0.0, 1.0 - cos_theta * cos_theta));
            let phi = 2.0 * PI * random();
            let half_vector = normalize(basis(normal) * vec3f(sin_theta * cos(phi), sin_theta * sin(phi), cos_theta));
            let next = reflect(direction, half_vector);
            let n_dot_l = dot(normal, next);
            let v_dot_h = max(dot(view, half_vector), 1e-4);
            let n_dot_h = max(dot(normal, half_vector), 1e-4);
            if n_dot_l <= 0.0 { break; }
            // BRDF * cos / pdf with pdf = D * (n.h) / (4 v.h).
            let weight = schlick(f0, v_dot_h) * smith_visibility(n_dot_v, n_dot_l, alpha) * 4.0 * v_dot_h * n_dot_l / n_dot_h;
            throughput *= weight / specular_chance;
            direction = next;
        } else {
            throughput *= diffuse * (vec3f(1.0) - fresnel_view) / (1.0 - specular_chance);
            direction = cosine_hemisphere(normal);
        }
        // A shading normal can send the path under the real surface; such a path carries no light.
        if dot(direction, face) <= 0.0 { break; }
        origin = surface;
        // The sun was already added through the direct-light sample.
        sees_sun_disk = false;

        if bounce >= 2u {
            let survive = clamp(max(throughput.x, max(throughput.y, throughput.z)), 0.05, 1.0);
            if random() > survive { break; }
            throughput /= survive;
        }
    }

    // A single sample far brighter than its neighbours would take thousands of samples to average out.
    let clamped = min(radiance, vec3f(64.0));
    accumulation[pixel] += vec4f(clamped, 1.0);
    atomicAdd(&ray_counter[0], rays);
}
