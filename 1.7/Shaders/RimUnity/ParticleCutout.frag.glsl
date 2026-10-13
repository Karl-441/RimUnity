#version 450
layout(location = 0) in vec2 uv;
layout(location = 1) in vec4 color;
layout(location = 0) out vec4 outColor;
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color; // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
    float _Cutoff; // @default = 0.5
} material;
layout(set = 2, binding = 1) uniform sampler2D _MainTex;
//对应 Standard Unlit 的 Cutout：裁切源透明度，保留像素写入不透明 Alpha。
void main()
{
    vec2 t = uv * material._MainTex_ST.zw + material._MainTex_ST.xy;
    vec4 albedo = texture(_MainTex, vec2(t.x, 1.0 - t.y)) * material._Color * color;
    if (albedo.a - material._Cutoff + 0.0001 < 0.0) discard;
    outColor = vec4(albedo.rgb, 1.0);
}
