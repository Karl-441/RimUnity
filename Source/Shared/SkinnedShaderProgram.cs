using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.Rendering;

//在已导出的顶点程序前执行蒙皮，原程序继续负责材质与投影。
internal static class SkinnedShaderProgram
{
    //仅接受明确的顶点语义，不按变量名称猜测位置或覆盖原 UV。
    internal static (string Vertex, string Fragment) Convert(string vertex, string fragment)
    {
        if (Regex.IsMatch(vertex + fragment, @"\b(?:RU_Skin\w*|_Bones|_SkinWeights)\b"))
            throw new InvalidDataException("蒙皮程序保留名称与源 Shader 冲突。");
        const string input = @"(?m)^([ \t]*layout\s*\(\s*location\s*=\s*(\d+)\s*\)\s*in\s+)(vec[234])\s+(\w+)\s*;[^\r\n]*?@vertex\s+(\w+)[^\r\n]*";
        var inputs = Regex.Matches(vertex, input).Cast<Match>().ToArray();
        if (inputs.Count(m => m.Groups[5].Value == "Position") != 1)
            throw new InvalidDataException("GPU 蒙皮需要唯一的显式 Position 输入。");
        if (inputs.Any(m => m.Groups[5].Value == "TexCoord6"))
            throw new InvalidDataException("源 Shader 已使用蒙皮权重专用 TexCoord6。");
        int location = Regex.Matches(vertex, @"layout\s*\(\s*location\s*=\s*(\d+)\s*\)\s*in\b").Cast<Match>()
            .Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;
        int binding = Regex.Matches(vertex + fragment, @"layout\s*\(([^)]*)\)").Cast<Match>()
            .Where(m => Regex.IsMatch(m.Groups[1].Value, @"\bset\s*=\s*2\b"))
            .Select(m => Regex.Match(m.Groups[1].Value, @"\bbinding\s*=\s*(\d+)"))
            .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max() + 1;
        string buffers = $"\nlayout(std430, set = 2, binding = {binding}) readonly buffer _Bones {{ mat4 matrices[]; }} RU_SkinBones;\n"
            + $"layout(std430, set = 2, binding = {binding + 1}) readonly buffer _SkinWeights {{ vec4 influences[]; }} RU_SkinWeights;\n";
        var assignments = new StringBuilder();
        foreach (var m in inputs.Where(m => m.Groups[5].Value is "Position" or "Normal" or "Tangent"))
        {
            string type = m.Groups[3].Value, name = m.Groups[4].Value, semantic = m.Groups[5].Value;
            if (semantic == "Position" && type is not ("vec3" or "vec4") || semantic == "Normal" && type != "vec3" || semantic == "Tangent" && type != "vec4")
                throw new InvalidDataException("蒙皮顶点语义维度不支持：" + semantic + "/" + type);
            string declaration = m.Value.Replace(type + " " + name, type + " RU_SkinInput_" + name);
            vertex = vertex.Replace(m.Value, declaration + "\n" + type + " " + name + ";");
            string original = "RU_SkinInput_" + name;
            string expression = semantic switch
            {
                "Position" => type == "vec4" ? "RU_SkinMatrix * " + original : "(RU_SkinMatrix * vec4(" + original + ", 1.0)).xyz",
                "Normal" => "normalize(mat3(RU_SkinMatrix) * " + original + ")",
                _ => "vec4(normalize(mat3(RU_SkinMatrix) * " + original + ".xyz), " + original + ".w)"
            };
            assignments.Append("    ").Append(name).Append(" = ").Append(expression).Append(";\n");
        }
        const string main = @"\bvoid\s+main\s*\(\s*\)";
        if (Regex.Matches(vertex, main).Count != 1) throw new InvalidDataException("GPU 蒙皮需要唯一 main 入口。");
        vertex = Regex.Replace(vertex, main, "void RU_SkinSourceMain()");
        vertex = Insert(vertex, buffers + $"layout(location = {location}) in vec4 RU_SkinRange; // @vertex TexCoord6 required\n");
        vertex += "\nvoid main()\n{\n    mat4 RU_SkinMatrix = mat4(0.0);\n"
            + "    for (int i = 0; i < int(RU_SkinRange.y); ++i)\n    {\n"
            + "        vec4 influence = RU_SkinWeights.influences[int(RU_SkinRange.x) + i];\n"
            + "        RU_SkinMatrix += RU_SkinBones.matrices[int(influence.x)] * influence.y;\n    }\n"
            + "    if (RU_SkinRange.y == 0.0) RU_SkinMatrix = mat4(1.0);\n"
            + assignments + "    RU_SkinSourceMain();\n}\n";
        fragment = Insert(fragment, buffers);
        ShaderBindingValidation.Validate(vertex, fragment);
        return (vertex, fragment);
    }

    //GLSL 版本声明必须保持首行；缓冲声明放在原程序使用之前。
    private static string Insert(string source, string declaration)
    {
        var version = Regex.Match(source, @"\A\s*#version[^\r\n]*(?:\r?\n)");
        if (!version.Success) throw new InvalidDataException("蒙皮输入缺少 GLSL version 声明。");
        return source.Insert(version.Length, declaration);
    }
}
