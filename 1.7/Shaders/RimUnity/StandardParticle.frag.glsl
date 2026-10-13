#version 450
layout(location = 0) in vec4 uv;
layout(location = 1) in vec4 color;
layout(location = 2) in float frameBlend;
layout(location = 3) in vec3 worldPosition;
layout(location = 0) out vec4 outColor;
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color; // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
    vec4 _EmissionColor; // @color @default = vec4(0.0, 0.0, 0.0, 1.0)
    vec4 _ColorAddSubDiff; // @default = vec4(0.0, 0.0, 0.0, 0.0)
    mat4 _RimUnity_View;
    float _Mode; // @default = 2.0
    float _ColorMode; // @default = 0.0
    float _FlipbookMode; // @default = 0.0
    float _CameraFadingEnabled; // @default = 0.0
    float _CameraNearFadeDistance; // @default = 1.0
    float _CameraFarFadeDistance; // @default = 2.0
    float _EmissionEnabled; // @default = 0.0
    float _Cutoff; // @default = 0.5
} material;
layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _EmissionMap;

//按照 Unity StandardParticles 的颜色模式保留亮度和粒子透明度。
vec3 rgbToHsv(vec3 c)
{
    vec4 k = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, k.wz), vec4(c.gb, k.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-10)), d / (q.x + 1e-10), q.x);
}
vec3 hsvToRgb(vec3 c)
{
    vec3 p = abs(fract(c.xxx + vec3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}
vec4 sampleFrame(sampler2D source)
{
    vec2 first = uv.xy;
    if (material._FlipbookMode < 0.5) first = first * material._MainTex_ST.zw + material._MainTex_ST.xy;
    vec4 value = texture(source, vec2(first.x, 1.0 - first.y));
    if (material._FlipbookMode > 0.5) value = mix(value, texture(source, vec2(uv.z, 1.0 - uv.w)), frameBlend);
    return value;
}
void main()
{
    vec4 value = sampleFrame(_MainTex) * material._Color;
    int mode = int(material._ColorMode);
    if (mode == 0) value *= color;
    else
    {
        if (mode == 2) value.rgb = mix(1.0 - 2.0 * (1.0 - value.rgb) * (1.0 - color.rgb), 2.0 * value.rgb * color.rgb, step(value.rgb, vec3(0.5)));
        else if (mode == 3) { vec3 a = rgbToHsv(value.rgb), b = rgbToHsv(color.rgb); value.rgb = hsvToRgb(vec3(b.xy, a.z)); }
        else { value.rgb += color.rgb * material._ColorAddSubDiff.x; value.rgb = mix(value.rgb, abs(value.rgb), material._ColorAddSubDiff.y); }
        value.a *= color.a;
    }
    float fade = 1.0;
    if (material._CameraFadingEnabled > 0.5)
    {
        float depth = -(material._RimUnity_View * vec4(worldPosition, 1.0)).z;
        fade = clamp((depth - material._CameraNearFadeDistance) / max(1e-6, material._CameraFarFadeDistance - material._CameraNearFadeDistance), 0.0, 1.0);
        if (int(material._Mode) == 3) value *= fade; else value.a *= fade;
    }
    if (int(material._Mode) == 1 && value.a - material._Cutoff + 0.0001 < 0.0) discard;
    if (int(material._Mode) == 6) value.rgb = mix(vec3(1.0), value.rgb, value.a);
    if (material._EmissionEnabled > 0.5) value.rgb += sampleFrame(_EmissionMap).rgb * material._EmissionColor.rgb * fade;
    if (int(material._Mode) <= 1 || int(material._Mode) == 6) value.a = 1.0;
    outColor = value;
}
