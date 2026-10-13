#version 450
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sourceImage;
layout(push_constant) uniform Settings { vec4 values; } settings;
//先解码再提取亮部，Alpha 不参与亮度阈值。
vec3 decode(vec3 c) { return mix(c / 12.92, pow(max((c + .055) / 1.055, vec3(0)), vec3(2.4)), step(vec3(.04045), c)); }
vec3 bright(vec2 uv)
{
    vec3 c = max(texture(sourceImage, uv).rgb, vec3(0));
    if (settings.values.z > .5) c = decode(c);
    c = min(c, vec3(65504));
    float b = max(c.r, max(c.g, c.b));
    float knee = max(settings.values.x * settings.values.y, .00001);
    float soft = clamp(b - settings.values.x + knee, 0, 2 * knee);
    soft = soft * soft / (4 * knee);
    return c * max(soft, b - settings.values.x) / max(b, .00001);
}
void main()
{
    vec2 d = .5 / vec2(textureSize(sourceImage, 0));
    outColor = vec4((bright(vUv + d) + bright(vUv - d) + bright(vUv + vec2(d.x, -d.y)) + bright(vUv + vec2(-d.x, d.y))) * .25, 1);
}
