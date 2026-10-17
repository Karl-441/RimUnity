using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//手写 ForwardBase 使用与 Surface 材质相同的主方向光和阴影附件。
internal static class UnityForwardLighting
{
    //仅接管明确引用 Lighting 的程序，其余光照模型继续使用各自的转换入口。
    internal static string Adapt(ShaderLabSource shader)
    {
        const string include = @"(?m)^\s*#include\s+[""<]Lighting\.cginc["">][^\r\n]*";
        string source = shader.Program;
        if (!Regex.IsMatch(source, include)) return source;
        if (shader.Manifest.profile != "world")
            throw new NotSupportedException("Lighting.cginc 需要世界绘制配置。");
        if (shader.Manifest.tags.GetValueOrDefault("LightMode") != "ForwardBase")
            throw new NotSupportedException("Lighting.cginc 当前只支持 ForwardBase 主方向光 Pass。");
        string unsupported = shader.Manifest.keywords.FirstOrDefault(k => k is "POINT" or "SPOT" or "POINT_COOKIE"
            or "DIRECTIONAL_COOKIE" or "SHADOWS_CUBE" or "LIGHTMAP_ON" or "DYNAMICLIGHTMAP_ON" or "SHADOWS_SHADOWMASK");
        if (unsupported != null) throw new NotSupportedException("ForwardBase 尚未支持该光照变体：" + unsupported);
        source = Regex.Replace(source, include, "");
        bool shadows = Regex.IsMatch(source, @"\b(?:SHADOW_COORDS|TRANSFER_SHADOW|SHADOW_ATTENUATION)\s*\(");
        shader.Manifest.approximations.Add("ForwardBase 主光源使用 RimUnity 的地图主方向光或离屏预制体方向光；不提供 Unity 烘焙光照和附加灯光 Pass。");
        if (shadows) shader.Manifest.approximations.Add("ForwardBase 接收 RimUnity 太阳或离屏方向光阴影，沿用深度偏移、强度和九点 PCF；月光与现有 Surface 路径一样不接收阴影，不使用 Unity 屏幕空间阴影纹理。");
        return Lighting + (shadows ? SurfaceShadows.Program + "\n" + ShadowMacros : "") + "\n" + source;
    }

    //运行时已为地图和离屏相机上传同一组方向光参数，颜色按目标空间计算。
    private const string Lighting = """
        float4 _RU_MapSunDirection, _RU_MapMoonDirection, _RU_MapSunColor, _RU_MapMoonColor;
        float4 RU_ForwardLightColor() {
            float4 color = any(_RU_MapSunColor.rgb) ? _RU_MapSunColor : _RU_MapMoonColor;
        #ifndef UNITY_COLORSPACE_GAMMA
            color.rgb = GammaToLinearSpace(color.rgb);
        #endif
            return color;
        }
        #define _WorldSpaceLightPos0 (any(_RU_MapSunColor.rgb) ? _RU_MapSunDirection : _RU_MapMoonDirection)
        #define _LightColor0 RU_ForwardLightColor()
        """ + "\n";

    //与 Unity 宏保持相同的顶点变量和单个 TEXCOORD 约定，保留真实阴影采样。
    private const string ShadowMacros = """
        #define SHADOW_COORDS(index) float4 _ShadowCoord : TEXCOORD##index;
        #define TRANSFER_SHADOW(output) output._ShadowCoord = mul(_RU_ShadowVP, mul(unity_ObjectToWorld, v.vertex))
        //月光方向与太阳阴影附件不同，不把太阳阴影错误地用于月光。
        #define SHADOW_ATTENUATION(input) (any(_RU_MapSunColor.rgb) ? RU_ShadowAttenuation(input._ShadowCoord) : 1.0)
        """ + "\n";
}
