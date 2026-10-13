#version 450
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color; // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
} material;
layout(set = 2, binding = 1) uniform sampler2D _MainTex;
//宿主从片元源解析完整材质布局，顶点专用缓冲也必须在这里声明。
layout(std430, set = 2, binding = 2) readonly buffer _Bones { mat4 matrices[]; } bones;
layout(std430, set = 2, binding = 3) readonly buffer _SkinWeights { vec4 influences[]; } weights;
void main()
{
    vec2 sampleUv = uv * material._MainTex_ST.zw + material._MainTex_ST.xy;
    outColor = texture(_MainTex, vec2(sampleUv.x, 1.0 - sampleUv.y)) * material._Color;
}
