#version 450
// World-space particle material: deliberately has no UI mask/storage capability.
layout(location = 0) in vec2 uv;
layout(location = 1) in vec4 color;
layout(location = 0) out vec4 outColor;
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color; // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
} material;
layout(set = 2, binding = 1) uniform sampler2D _MainTex;
void main()
{
    vec2 t = uv * material._MainTex_ST.zw + material._MainTex_ST.xy;
    outColor = texture(_MainTex, vec2(t.x, 1.0 - t.y)) * material._Color * color;
}
