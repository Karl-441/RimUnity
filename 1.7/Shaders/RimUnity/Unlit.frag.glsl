#version 450
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color; // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
} material;
layout(set = 2, binding = 1) uniform sampler2D _MainTex;

//兼容层上传时已把 _ST 调整为偏移、缩放，UV 只在采样时翻转一次。
void main()
{
    vec2 sampleUv = uv * material._MainTex_ST.zw + material._MainTex_ST.xy;
    outColor = texture(_MainTex, vec2(sampleUv.x, 1.0 - sampleUv.y)) * material._Color;
}
