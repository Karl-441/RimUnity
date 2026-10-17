using System;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//用真实对象和相机矩阵恢复 Unity 的仿射模型视图变换。
internal static class UnityMatrixCompat
{
    //法线必须经过逆转置，包含非均匀缩放、剪切和镜像时也保持原语义。
    internal static string Adapt(string source, string profile)
    {
        if (!Regex.IsMatch(source, @"\bUNITY_MATRIX_IT_MV\b")) return source;
        if (profile != "world")
            throw new NotSupportedException("UNITY_MATRIX_IT_MV 需要世界绘制的对象与相机矩阵，屏幕 UI 顶点不提供该变换。");
        source = Regex.Replace(source, @"\bUNITY_MATRIX_IT_MV\b",
            "RU_InverseTransposeAffine(mul(_RimUnity_View, unity_ObjectToWorld))");
        //模型视图矩阵末行是 (0,0,0,1)，保留完整四阶逆转置以支持齐次平面。
        return """
            float4x4 RU_InverseTransposeAffine(float4x4 m)
            {
                float3 c0 = cross(m[1].xyz, m[2].xyz);
                float3 c1 = cross(m[2].xyz, m[0].xyz);
                float3 c2 = cross(m[0].xyz, m[1].xyz);
                float determinant = dot(m[0].xyz, c0);
                float3 lastRow = -(m[0].w * c0 + m[1].w * c1 + m[2].w * c2);
                return float4x4(float4(c0 / determinant, 0),
                    float4(c1 / determinant, 0), float4(c2 / determinant, 0),
                    float4(lastRow / determinant, 1));
            }
            """ + "\n" + source;
    }
}
