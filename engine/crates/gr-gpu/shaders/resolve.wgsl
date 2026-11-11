// Turns the accumulated radiance into the picture shown on screen: average, exposure, filmic curve, sRGB encoding.

struct Params {
    width: u32,
    height: u32,
    exposure: f32,
    pad: f32,
}

@group(0) @binding(0) var<uniform> params: Params;
@group(0) @binding(1) var<storage, read> accumulation: array<vec4f>;
@group(0) @binding(2) var output: texture_storage_2d<rgba8unorm, write>;

fn filmic(x: vec3f) -> vec3f {
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), vec3f(0.0), vec3f(1.0));
}

fn srgb(linear: vec3f) -> vec3f {
    let low = linear * 12.92;
    let high = 1.055 * pow(linear, vec3f(1.0 / 2.4)) - 0.055;
    return select(high, low, linear <= vec3f(0.0031308));
}

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) id: vec3u) {
    if id.x >= params.width || id.y >= params.height { return; }
    let sample = accumulation[id.y * params.width + id.x];
    let radiance = sample.rgb / max(sample.a, 1.0);
    textureStore(output, vec2i(id.xy), vec4f(srgb(filmic(max(radiance * params.exposure, vec3f(0.0)))), 1.0));
}
