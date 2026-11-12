// Edge-avoiding A-trous wavelet filter (Dammertz et al., 2010): one pass of a 5 x 5 B-spline kernel whose taps
// are `step` pixels apart. Run with steps 1, 2, 4, 8 it gathers light from a 61-pixel neighbourhood at the cost
// of 100 taps. A tap only contributes when it lies on the same surface as the centre (normal and plane distance,
// from the first hit of each pixel) and is not far brighter or darker than it.

struct Params {
    camera_position: vec3f,
    width: u32,
    camera_right: vec3f,
    height: u32,
    camera_up: vec3f,
    step: i32,
    camera_forward: vec3f,
    // Tolerated colour difference; it shrinks as samples accumulate, so a converged image passes through unchanged.
    color_sigma: f32,
}

@group(0) @binding(0) var<uniform> params: Params;
// rgb is a sum of samples and a their number; the passes after the first read means with a = 1.
@group(0) @binding(1) var<storage, read> source: array<vec4f>;
// xyz is the shading normal of the first hit and w its distance along the ray; w < 0 is the sky.
@group(0) @binding(2) var<storage, read> features: array<vec4f>;
@group(0) @binding(3) var<storage, read_write> filtered: array<vec4f>;

fn mean(sample: vec4f) -> vec3f {
    return sample.rgb / max(sample.a, 1.0);
}

// Compresses bright values so one very bright neighbour cannot outweigh the rest.
fn compress(colour: vec3f) -> vec3f {
    return colour / (1.0 + dot(colour, vec3f(0.2126, 0.7152, 0.0722)));
}

fn world_position(pixel: vec2i, distance: f32) -> vec3f {
    let x = (f32(pixel.x) + 0.5) / f32(params.width) * 2.0 - 1.0;
    let y = 1.0 - (f32(pixel.y) + 0.5) / f32(params.height) * 2.0;
    return params.camera_position + normalize(params.camera_forward + params.camera_right * x + params.camera_up * y) * distance;
}

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) id: vec3u) {
    if id.x >= params.width || id.y >= params.height { return; }
    let pixel = vec2i(id.xy);
    let index = id.y * params.width + id.x;
    let centre = mean(source[index]);
    let guide = features[index];
    if guide.w < 0.0 {
        // The sky has no noise of its own.
        filtered[index] = vec4f(centre, 1.0);
        return;
    }
    let position = world_position(pixel, guide.w);
    let centre_compressed = compress(centre);
    // How far off the surface plane a neighbour may be: a little more with distance, where a pixel covers more.
    let plane_tolerance = 0.002 + 0.004 * guide.w * f32(params.step);
    let kernel = array<f32, 5>(0.0625, 0.25, 0.375, 0.25, 0.0625);

    var sum = vec3f(0.0);
    var weight_sum = 0.0;
    for (var dy = -2; dy <= 2; dy++) {
        for (var dx = -2; dx <= 2; dx++) {
            let tap = pixel + vec2i(dx, dy) * params.step;
            if tap.x < 0 || tap.y < 0 || tap.x >= i32(params.width) || tap.y >= i32(params.height) { continue; }
            let tap_index = u32(tap.y) * params.width + u32(tap.x);
            let tap_guide = features[tap_index];
            if tap_guide.w < 0.0 { continue; }
            let colour = mean(source[tap_index]);
            let same_facing = pow(max(dot(guide.xyz, tap_guide.xyz), 0.0), 48.0);
            let off_plane = abs(dot(guide.xyz, world_position(tap, tap_guide.w) - position));
            let same_plane = exp(-off_plane / plane_tolerance);
            let difference = compress(colour) - centre_compressed;
            let similar = exp(-dot(difference, difference) / (params.color_sigma * params.color_sigma + 1e-8));
            let weight = kernel[dx + 2] * kernel[dy + 2] * same_facing * same_plane * similar;
            sum += colour * weight;
            weight_sum += weight;
        }
    }
    filtered[index] = vec4f(select(centre, sum / weight_sum, weight_sum > 1e-6), 1.0);
}
