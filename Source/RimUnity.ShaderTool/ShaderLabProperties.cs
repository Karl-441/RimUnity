using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RimUnity.ShaderTool;

//材质默认值与范围直接来自 Properties，不从着色器名称猜测。
internal static class ShaderLabProperties
{
    //读取标量、向量和贴图属性，并保留 Unity 特性用于导出检查。
    internal static void Read(ShaderLabTokens tokens, ShaderLabManifest manifest)
    {
        tokens.Expect("{");
        while (!tokens.Take("}"))
        {
            List<string> attributes = new();
            while (tokens.Peek == "[") attributes.Add(tokens.Balanced("[", "]"));
            ShaderLabProperty property = new() { name = tokens.Read(), attributes = attributes.ToArray() };
            if (manifest.properties.Any(p => p.name == property.name)) throw new InvalidDataException("重复材质属性：" + property.name);
            tokens.Expect("("); property.displayName = tokens.String(); tokens.Expect(",");
            property.type = tokens.Read();
            if (property.type.Equals("Range", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Expect("("); float min = Number(tokens); tokens.Expect(","); float max = Number(tokens);
                tokens.Expect(")"); property.range = new[] { min, max };
            }
            tokens.Expect(")"); tokens.Expect("=");
            switch (property.type.ToLowerInvariant())
            {
                case "float": case "range": case "int": case "integer": property.defaultValue = new[] { Number(tokens) }; break;
                case "color": case "vector":
                    tokens.Expect("("); property.defaultValue = new float[4];
                    for (int i = 0; i < 4; i++) { if (i > 0) tokens.Expect(","); property.defaultValue[i] = Number(tokens); }
                    tokens.Expect(")"); break;
                case "2d": case "cube": case "3d": case "2darray": case "cubearray":
                    property.defaultTexture = tokens.String();
                    if (tokens.Balanced("{", "}").Length != 0) throw new NotSupportedException("不支持旧式纹理选项块：" + property.name);
                    break;
                default: throw new NotSupportedException("未支持的材质属性类型：" + property.type);
            }
            manifest.properties.Add(property);
        }
    }

    //仅接受有限的数字，不将表达式或无效默认值替换成零。
    private static float Number(ShaderLabTokens tokens)
    {
        string text = tokens.Read();
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new InvalidDataException("无效属性数值：" + text);
        return value;
    }
}
