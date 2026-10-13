using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//以适配器记录的绑定号恢复原属性名，不依赖 Slang 的临时变量命名。
internal static class ShaderLabGlsl
{
    //游戏 GLSL 前端需要合并采样器，材质反射需要原始成员名和规范矩阵类型。
    internal static string Rewrite(string glsl, string hlsl)
    {
        Match fields = Regex.Match(hlsl, @"// @rimunity-material ([^\r\n]+)");
        if (fields.Success)
        {
            Match block = Regex.Matches(glsl, @"layout\(([^)]*)\)\s*uniform\s+\w+\s*\{([^{}]*)\}").Cast<Match>()
                .SingleOrDefault(m => Binding(m.Groups[1].Value, 0));
            if (block != null)
            {
                string[] originals = fields.Groups[1].Value.Split(',');
                Match[] members = Regex.Matches(block.Groups[2].Value, @"\b\w+\s+(\w+)\s*;").Cast<Match>().ToArray();
                if (members.Length != originals.Length) throw new InvalidDataException("Slang 输出的材质块成员数量与源布局不一致。");
                for (int i = 0; i < originals.Length; i++) glsl = Rename(glsl, members[i].Groups[1].Value, originals[i]);
            }
        }
        foreach (Match texture in Regex.Matches(hlsl, @"// @rimunity-texture (\d+) (\w+)"))
        {
            Match declaration = Resource(glsl, int.Parse(texture.Groups[1].Value));
            if (declaration != null) glsl = Rename(glsl, declaration.Groups[3].Value, texture.Groups[2].Value);
        }
        foreach (Match pair in Regex.Matches(hlsl, @"// @rimunity-combined (\d+) (\d+)"))
        {
            Match texture = Resource(glsl, int.Parse(pair.Groups[1].Value)), sampler = Resource(glsl, int.Parse(pair.Groups[2].Value));
            if (texture == null && sampler == null) continue;
            if (texture == null || sampler == null) throw new InvalidDataException("纹理和采样器没有成对出现在 Slang 输出。");
            string textureName = texture.Groups[3].Value, samplerName = sampler.Groups[3].Value;
            glsl = glsl.Replace(texture.Value, texture.Value.Replace("texture2D", "sampler2D")).Replace(sampler.Value, "");
            glsl = Regex.Replace(glsl, @"\bsampler2D\s*\(\s*" + Regex.Escape(textureName) + @"\s*,\s*" + Regex.Escape(samplerName) + @"\s*\)", textureName);
            if (Regex.IsMatch(glsl, @"\b" + Regex.Escape(samplerName) + @"\b"))
                throw new NotSupportedException("采样器被跨纹理共享或传入自定义函数，需要显式迁移：" + samplerName);
        }
        glsl = Regex.Replace(glsl, @"\bmat([234])x\1\b", "mat$1");
        if (hlsl.Contains("@rimunity-material") || hlsl.Contains("@rimunity-texture"))
        {
            foreach (Match block in Regex.Matches(glsl, @"layout\(([^)]*)\)\s*uniform\s+(\w+)\s*\{"))
                if (!Binding(block.Groups[1].Value, 0) && !Regex.IsMatch(block.Groups[2].Value, @"^block_RU_(CameraData|FrameData|ObjectData)_\d+$"))
                    throw new NotSupportedException("出现未适配的全局 uniform 块：" + block.Groups[2].Value + "。请使用独立的标量／向量／矩阵声明。");
        }
        return HostMaterialVectors(HostMatrices(glsl));
    }

    //宿主给 vec3 保留 16 字节，而 GLSL 允许紧接标量；使用 vec4 存储并在读取时恢复 xyz。
    internal static string HostMaterialVectors(string glsl)
    {
        var accesses = new System.Collections.Generic.List<string>();
        glsl = Regex.Replace(glsl, @"layout\(([^)]*)\)\s*uniform\s+\w+\s*\{([^{}]*)\}\s*(\w+)\s*;", block =>
        {
            if (!Binding(block.Groups[1].Value, 0)) return block.Value;
            string body = Regex.Replace(block.Groups[2].Value, @"\bvec3\s+(\w+)\s*;([^\r\n]*)", member =>
            {
                accesses.Add(Regex.Escape(block.Groups[3].Value) + @"\." + Regex.Escape(member.Groups[1].Value));
                string annotation = Regex.Replace(member.Groups[2].Value, @"(@default\s*=\s*)vec3\(([^)]*)\)", value =>
                {
                    string[] parts = value.Groups[2].Value.Split(',');
                    string xyz = parts.Length == 1 ? string.Join(", ", Enumerable.Repeat(parts[0], 3)) : value.Groups[2].Value;
                    return value.Groups[1].Value + "vec4(" + xyz + ", 0)";
                });
                return "vec4 " + member.Groups[1].Value + ";" + annotation;
            });
            return block.Value.Replace(block.Groups[2].Value, body);
        });
        foreach (string access in accesses)
            glsl = Regex.Replace(glsl, @"\b" + access + @"\b", match => "(" + match.Value + ".xyz)");
        return glsl;
    }

    //宿主 GLSL 前端把矩阵成员编译成 RowMajor；HLSL 后端生成的向量右乘需在读取处转置一次。
    //局部矩阵和乘法顺序仍由 Slang 维护，不能全局替换乘法表达式。
    internal static string HostMatrices(string glsl)
    {
        const string marker = "// @rimunity-host-matrices 1";
        if (glsl.Contains(marker)) return glsl;
        var blocks = Regex.Matches(glsl, @"layout\([^)]*\)\s*uniform\s+\w+\s*\{([^{}]*)\}\s*(\w+)\s*;");
        foreach (Match block in blocks)
            foreach (Match member in Regex.Matches(block.Groups[1].Value, @"\bmat[234]\s+(\w+)\s*;"))
            {
                string access = Regex.Escape(block.Groups[2].Value) + @"\." + Regex.Escape(member.Groups[1].Value);
                glsl = Regex.Replace(glsl, @"\b" + access + @"\b", match => "transpose(" + match.Value + ")");
            }
        return glsl + "\n" + marker + "\n";
    }

    //只匹配材质 set，绑定号不会与帧块或 push constant 混淆。
    private static bool Binding(string layout, int binding) => Regex.IsMatch(layout, @"\bset\s*=\s*2\b")
        && Regex.IsMatch(layout, @"\bbinding\s*=\s*" + binding + @"\b");
    //资源声明允许跨行，layout 已由基础编译器归并。
    private static Match Resource(string glsl, int binding) => Regex.Matches(glsl, @"layout\(([^)]*)\)\s*uniform\s+(texture2D|sampler)\s+(\w+)\s*;").Cast<Match>()
        .SingleOrDefault(m => Binding(m.Groups[1].Value, binding));
    //同步更名声明与使用处，避免局部结构副本保留旧成员名。
    private static string Rename(string source, string before, string after) => Regex.Replace(source, @"\b" + Regex.Escape(before) + @"\b", after);
}
