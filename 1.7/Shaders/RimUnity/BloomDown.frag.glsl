#version 450
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sourceImage;
//双线性四点降采样，后续各层保持线性浮点光强。
void main()
{
    vec2 d = 1.0 / vec2(textureSize(sourceImage, 0));
    outColor = (texture(sourceImage, vUv + d) + texture(sourceImage, vUv - d)
        + texture(sourceImage, vUv + vec2(d.x, -d.y)) + texture(sourceImage, vUv + vec2(-d.x, d.y))) * .25;
}
