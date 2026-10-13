using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimUnity.Rendering;

//宿主 UNorm 场景使用显示颜色；Linear 程序在采样和输出边界显式转换，Alpha 始终保持原值。
internal static class ShaderColorSpace
{
    internal const string TextureFlag = "_RimUnitySrgb";
    private const string Marker = "// @rimunity-linear-program 1";

    //同时处理两阶段的共享材质块，避免顶点和片元各自增加字段后出现偏移分歧。
    internal static (string Vertex, string Fragment) Convert(string vertex, string fragment)
    {
        if (fragment.Contains(Marker)) return (vertex, fragment);
        vertex = SpecializeSamplers(vertex); fragment = SpecializeSamplers(fragment);
        var textures = Regex.Matches(vertex + "\n" + fragment, @"layout\([^)]*set\s*=\s*2[^)]*\)\s*uniform\s+sampler2D\s+(\w+)\s*;")
            .Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Match block = MaterialBlock(fragment);
        if (!block.Success) block = MaterialBlock(vertex);
        string declaration;
        if (block.Success)
        {
            declaration = block.Value;
            int end = declaration.LastIndexOf('}');
            declaration = declaration.Insert(end, string.Join("", textures.Select(t => "    float " + TextureFlag + t + "; // @default = 1\n")));
        }
        else declaration = "layout(set = 2, binding = 0) uniform RU_ColorSpaceMaterial {\n"
            + string.Join("", textures.Select(t => "    float " + TextureFlag + t + "; // @default = 1\n")) + "} ruColorSpace;";
        //没有纹理和材质常量的程序只需要输出变换。
        if (block.Success || textures.Length > 0)
        {
            vertex = SetBlock(vertex, declaration); fragment = SetBlock(fragment, declaration);
        }
        vertex = Adapt(vertex, textures, false);
        fragment = Adapt(fragment, textures, true);
        return (vertex, fragment);
    }

    //单纹理帮助函数按实参展开，保留 UV、插帧和采样逻辑，并让编码标记对应实际贴图。
    private static string SpecializeSamplers(string source)
    {
        var functions = Regex.Matches(source, @"\bvec4\s+(\w+)\s*\(\s*sampler2D\s+(\w+)\s*\)\s*\{");
        foreach (Match function in functions.Cast<Match>().Reverse())
        {
            int end = function.Index + function.Length, depth = 1;
            while (end < source.Length && depth != 0) { if (source[end] == '{') depth++; else if (source[end] == '}') depth--; end++; }
            if (depth != 0) throw new FormatException("纹理帮助函数括号不匹配。");
            string body = source.Substring(function.Index + function.Length, end - function.Index - function.Length - 1);
            source = source.Remove(function.Index, end - function.Index);
            var declarations = new List<string>();
            var seen = new HashSet<string>();
            source = Regex.Replace(source, @"\b" + Regex.Escape(function.Groups[1].Value) + @"\s*\(\s*(\w+)\s*\)", call =>
            {
                string texture = call.Groups[1].Value, name = function.Groups[1].Value + "_" + texture;
                if (seen.Add(texture)) declarations.Add("vec4 " + name + "() {" + Regex.Replace(body, @"\b" + Regex.Escape(function.Groups[2].Value) + @"\b", texture) + "}\n");
                return name + "()";
            });
            source = source.Insert(function.Index, string.Concat(declarations));
        }
        if (Regex.IsMatch(source, @"\([^;{}]*\bsampler2D\s+\w+[^;{}]*\)\s*\{"))
            throw new NotSupportedException("Linear 转换暂不接受多参数纹理函数，请将采样放到使用全局贴图的函数中。");
        return source;
    }

    //颜色属性和贴图的解码发生在读取处，脚本 SetColor/SetVector 仍传入原始 Unity 数值。
    private static string Adapt(string source, string[] textures, bool fragment)
    {
        Match block = MaterialBlock(source);
        string instance = block.Success ? block.Groups[2].Value : "";
        if (block.Success)
        {
            foreach (Match field in Regex.Matches(block.Groups[1].Value, @"\b(float|vec[234])\s+(\w+)\s*;[^\r\n]*@(?:color|rimunity-gamma)\b"))
                source = Regex.Replace(source, @"\b" + Regex.Escape(instance) + @"\." + Regex.Escape(field.Groups[2].Value) + @"\b", m => "RU_Linear(" + m.Value + ")");
        }
        //Slang 可以保留以 sampler2D 为参数的函数；这类调用须先内联，不能猜测参数指向的贴图。
        foreach (string texture in textures)
        {
            var calls = Regex.Matches(source, @"\btexture(?:Lod|Grad|Proj)?\s*\(\s*" + Regex.Escape(texture) + @"\s*,");
            foreach (Match call in calls.Cast<Match>().Reverse())
            {
                int end = call.Index + call.Length, depth = 1;
                while (end < source.Length && depth != 0)
                {
                    if (source[end] == '(') depth++; else if (source[end] == ')') depth--;
                    end++;
                }
                if (depth != 0) throw new FormatException("纹理采样表达式括号不匹配：" + texture);
                string sample = source.Substring(call.Index, end - call.Index);
                source = source.Remove(call.Index, end - call.Index).Insert(call.Index,
                    "RU_SampleLinear(" + sample + ", " + instance + "." + TextureFlag + texture + ")");
            }
        }
        source = source.Insert(source.IndexOf('\n') + 1, Helpers + "\n");
        if (fragment)
        {
            Match output = Regex.Match(source, @"layout\([^)]*location\s*=\s*0[^)]*\)\s*out\s+vec4\s+(\w+)\s*;");
            if (!output.Success) throw new NotSupportedException("Linear 转换需要 location 0 的 vec4 颜色输出。");
            source = Regex.Replace(source, @"\bvoid\s+main\s*\(", "void RU_SourceMain(");
            source += "\nvoid main() { RU_SourceMain(); " + output.Groups[1].Value + ".rgb = RU_Encode(" + output.Groups[1].Value + ".rgb); }\n";
        }
        return source + "\n" + Marker + "\n";
    }

    //匹配材质块时只接受 set 2、binding 0，不碰相机、蒙皮和对象常量。
    private static Match MaterialBlock(string source) => Regex.Matches(source, @"layout\(([^)]*)\)\s*uniform\s+\w+\s*\{([^{}]*)\}\s*(\w+)\s*;")
        .Cast<Match>().Where(m => Regex.IsMatch(m.Groups[1].Value, @"\bset\s*=\s*2\b") && Regex.IsMatch(m.Groups[1].Value, @"\bbinding\s*=\s*0\b"))
        .Select(m => Regex.Match(m.Value, @"layout\([^)]*\)\s*uniform\s+\w+\s*\{([^{}]*)\}\s*(\w+)\s*;"))
        .SingleOrDefault() ?? Match.Empty;

    //沿用一个实例名并同步使用处，两个阶段可以具有不同的编译器临时名称。
    private static string SetBlock(string source, string declaration)
    {
        Match old = MaterialBlock(source), next = MaterialBlock(declaration);
        if (!old.Success) return source.Insert(source.IndexOf('\n') + 1, declaration + "\n");
        source = source.Replace(old.Value, declaration);
        return Regex.Replace(source, @"\b" + Regex.Escape(old.Groups[2].Value) + @"\.", next.Groups[2].Value + ".");
    }

    private const string Helpers = """
        vec3 RU_Decode(vec3 c) {
            return mix(pow(max((c + 0.055) / 1.055, vec3(0.0)), vec3(2.4)), c / 12.92, lessThanEqual(c, vec3(0.04045)));
        }
        vec3 RU_Encode(vec3 c) {
            return mix(1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055, c * 12.92, lessThanEqual(c, vec3(0.0031308)));
        }
        vec4 RU_Linear(vec4 c) { return vec4(RU_Decode(c.rgb), c.a); }
        vec3 RU_Linear(vec3 c) { return RU_Decode(c); }
        vec2 RU_Linear(vec2 c) { return RU_Decode(vec3(c, 0.0)).xy; }
        float RU_Linear(float c) { return RU_Decode(vec3(c)).x; }
        vec4 RU_SampleLinear(vec4 c, float srgb) { return srgb > 0.5 ? RU_Linear(c) : c; }
        """;
}
