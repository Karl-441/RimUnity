#version 450
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D detailImage;
layout(set = 0, binding = 1) uniform sampler2D coarseImage;
layout(push_constant) uniform Settings { vec4 values; } settings;
//九点帐篷滤波重建，归一化混合避免增加层数就改变总亮度。
void main()
{
    vec2 d = 1.0 / vec2(textureSize(coarseImage, 0));
    vec3 c = texture(coarseImage, vUv).rgb * 4;
    c += (texture(coarseImage, vUv + vec2(d.x, 0)).rgb + texture(coarseImage, vUv - vec2(d.x, 0)).rgb
        + texture(coarseImage, vUv + vec2(0, d.y)).rgb + texture(coarseImage, vUv - vec2(0, d.y)).rgb) * 2;
    c += texture(coarseImage, vUv + d).rgb + texture(coarseImage, vUv - d).rgb
        + texture(coarseImage, vUv + vec2(d.x, -d.y)).rgb + texture(coarseImage, vUv + vec2(-d.x, d.y)).rgb;
    outColor = vec4(mix(texture(detailImage, vUv).rgb, c / 16, settings.values.x), 1);
}
