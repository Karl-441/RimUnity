using UnityEngine;
using Verse;

namespace RimUnity.Rendering;

internal static partial class ShaderGlobals
{
    // Immutable IDs only. Light values are refreshed once per frame by Update;
    // no map references or material instances are retained across game sessions.
    private static readonly int SunDirectionId = Shader.PropertyToID("_RU_MapSunDirection"), MoonDirectionId = Shader.PropertyToID("_RU_MapMoonDirection"),
        SunColorId = Shader.PropertyToID("_RU_MapSunColor"), MoonColorId = Shader.PropertyToID("_RU_MapMoonColor"), AmbientId = Shader.PropertyToID("_RU_MapAmbient");

    private static void UpdateMapLighting()
    {
        Map map = Find.CurrentMap;
        Vector4 sun = Vector4.zero, moon = Vector4.zero;
        Color ambient = Color.black, radiance = Color.black;
        if (map != null)
        {
            // SkyManager already publishes these celestial values; do not recalculate
            // astronomy or scan Unity lights in the renderer.
            sun = Shader.GetGlobalVector(ShaderPropertyIDs.WaterCastVectSun);
            moon = Shader.GetGlobalVector(ShaderPropertyIDs.WaterCastVectMoon);
            ambient = radiance = map.skyManager.CurSky.colors.sky;
            if (map.Biome != null && map.Biome.disableSkyLighting)
            { ambient = Color.white; sun.w = moon.w = 0; }
        }
        Vector3 sunDir = new Vector3(-sun.x, 1, -sun.z).normalized;
        Vector3 moonDir = new Vector3(-moon.x, 1, -moon.z).normalized;
        Shader.SetGlobalVector(SunDirectionId, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0));
        Shader.SetGlobalVector(MoonDirectionId, new Vector4(moonDir.x, moonDir.y, moonDir.z, 0));
        Shader.SetGlobalVector(SunColorId, new Vector4(radiance.r * sun.w, radiance.g * sun.w, radiance.b * sun.w, 0));
        Shader.SetGlobalVector(MoonColorId, new Vector4(radiance.r * moon.w, radiance.g * moon.w, radiance.b * moon.w, 0));
        Shader.SetGlobalVector(AmbientId, new Vector4(ambient.r, ambient.g, ambient.b, 0));
    }
}