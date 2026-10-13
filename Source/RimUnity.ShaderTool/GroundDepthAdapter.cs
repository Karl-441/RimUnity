using System;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//以地图地表的解析深度恢复软交界，不冒充包含建筑和其他模型的场景深度。
internal static class GroundDepthAdapter
{
    //在变体预处理后替换实际深度采样，普通材质贴图仍走原来的纹理路径。
    internal static string Adapt(string source, ShaderLabManifest manifest)
    {
        if (!Regex.IsMatch(source, @"\b_CameraDepthTexture\b")) return source;
        string withoutDeclaration = Regex.Replace(source, @"\b(?:uniform\s+)?sampler2D\s+_CameraDepthTexture\s*;", "");
        if (!Regex.IsMatch(withoutDeclaration, @"\b_CameraDepthTexture\b")) return withoutDeclaration;
        if (manifest.profile != "world") throw new NotSupportedException("地表软粒子目前只用于世界绘制，不能用于 Overlay UI。");
        foreach (string function in new[] { "tex2D", "tex2Dproj", "tex2Dlod", "tex2Dbias" })
            withoutDeclaration = Regex.Replace(withoutDeclaration, @"\b" + function + @"\s*\(\s*_CameraDepthTexture\s*,", "RU_Ground_" + function + "(");
        if (Regex.IsMatch(withoutDeclaration, @"\b_CameraDepthTexture\b"))
            throw new NotSupportedException("此相机深度访问不能转为地表采样，需单独适配。");
        manifest.approximations.Add("软粒子深度使用世界 Y=0 地表；不包含建筑、模型或其他粒子的遮挡。");
        return Helpers + "\n" + withoutDeclaration;
    }

    //相机使用 0..1 反向 Z；直接沿像素射线求地表交点，不分配或逐帧上传深度纹理。
    private const string Helpers = """
        float4x4 _RimUnity_DepthInvVP;
        float4x4 _RimUnity_DepthVP;
        float RU_GroundDepth(float2 uv)
        {
            float2 xy = uv * 2.0 - 1.0;
            float4 nearH = mul(_RimUnity_DepthInvVP, float4(xy, 1.0, 1.0));
            float4 farH = mul(_RimUnity_DepthInvVP, float4(xy, 0.0, 1.0));
            float3 nearP = nearH.xyz / nearH.w;
            float3 farP = farH.xyz / farH.w;
            float dy = farP.y - nearP.y;
            if (abs(dy) < 0.000001) return 0.0;
            float t = -nearP.y / dy;
            if (t < 0.0 || t > 1.0) return 0.0;
            float4 hit = mul(_RimUnity_DepthVP, float4(lerp(nearP, farP, t), 1.0));
            return saturate(hit.z / hit.w);
        }
        float4 RU_Ground_tex2D(float2 uv) { return RU_GroundDepth(uv).xxxx; }
        float4 RU_Ground_tex2D(float3 uv) { return RU_Ground_tex2D(uv.xy); }
        float4 RU_Ground_tex2D(float4 uv) { return RU_Ground_tex2D(uv.xy); }
        float4 RU_Ground_tex2Dproj(float4 uv) { return RU_Ground_tex2D(uv.xy / uv.w); }
        float4 RU_Ground_tex2Dlod(float4 uv) { return RU_Ground_tex2D(uv.xy); }
        float4 RU_Ground_tex2Dbias(float4 uv) { return RU_Ground_tex2D(uv.xy); }
        """;
}
