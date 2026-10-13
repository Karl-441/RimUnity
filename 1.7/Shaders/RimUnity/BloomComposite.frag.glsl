#version 450
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sceneImage;
layout(set = 0, binding = 1) uniform sampler2D bloomImage;
layout(push_constant) uniform Settings { vec4 values; } settings;
vec3 decode(vec3 c) { return mix(c / 12.92, pow(max((c + .055) / 1.055, vec3(0)), vec3(2.4)), step(vec3(.04045), c)); }
vec3 encode(vec3 c) { return mix(c * 12.92, 1.055 * pow(max(c, vec3(0)), vec3(1.0 / 2.4)) - .055, step(vec3(.0031308), c)); }
//只加上光晕带来的差值，保留宿主已有合成与屏幕 Alpha。
void main()
{
    vec3 original = texture(sceneImage, vUv).rgb;
    vec3 added = texture(bloomImage, vUv).rgb * settings.values.x;
    vec3 delta = settings.values.y > .5 ? encode(decode(max(original, vec3(0))) + added) - original : added;
    float luma = dot(delta, vec3(.2126, .7152, .0722));
    outColor = vec4(mix(vec3(luma), delta, settings.values.z), 0);
}
