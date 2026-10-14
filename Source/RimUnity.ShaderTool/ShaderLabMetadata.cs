using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//补齐 Waylith 依赖的属性元数据，不改变 Slang 生成的计算程序。
internal static class ShaderLabMetadata
{
    //属性名称必须与 Unity Material API 一致，默认值按材质块原生布局填写。
    internal static string Apply(string glsl, ShaderLabManifest manifest)
    {
        foreach (ShaderLabProperty property in manifest.properties)
        {
            if (property.defaultValue != null)
            {
                glsl = Regex.Replace(glsl, @"(?m)^(\s*(float|int|uint|vec[234]|ivec[234]|uvec[234])\s+" + Regex.Escape(property.name) + @"\s*;)[^\S\r\n]*$", match =>
                {
                    string type = match.Groups[2].Value;
                    int width = char.IsDigit(type[^1]) ? type[^1] - '0' : 1;
                    string value = string.Join(", ", property.defaultValue.Take(width).Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
                    if (width > 1) value = type + "(" + value + ")";
                    bool color = property.type.Equals("Color", StringComparison.OrdinalIgnoreCase);
                    bool gamma = color || (property.attributes?.Any(a => a.Equals("Gamma", StringComparison.OrdinalIgnoreCase)) ?? false);
                    return match.Groups[1].Value + " // @default = " + value + (color && width == 4 ? " @color" : gamma ? " @rimunity-gamma" : "");
                });
            }
            else if (property.defaultTexture != null)
            {
                string textureDefault = property.type.Equals("Cube", StringComparison.OrdinalIgnoreCase) ? "gray" : property.defaultTexture switch { "white" or "" => "white", "black" => "black", "gray" => "gray", _ => null };
                if (textureDefault != null) glsl = Regex.Replace(glsl, @"(?m)^(layout\([^\r\n]+\)\s+uniform\s+\w+\s+" + Regex.Escape(property.name) + @"\s*;)[^\S\r\n]*$", "$1 // @default = " + textureDefault);
            }
        }
        //包括 Surface 自动生成的虚拟 UV 变换，原生布局始终是偏移在前、缩放在后。
        return Regex.Replace(glsl, @"(?m)^(\s*vec4\s+\w+_ST\s*;)[^\S\r\n]*$", "$1 // @default = vec4(0, 0, 1, 1)");
    }
}

