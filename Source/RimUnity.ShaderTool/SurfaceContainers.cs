using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//把共享包含并入唯一 Surface 程序，只剥离不由颜色绘制提交的 ShadowCaster。
internal static class SurfaceContainers
{
    internal static string ColorProgram(string source)
    {
        var blocks = new List<string>();
        string masked = Regex.Replace(source, @"\b(CGPROGRAM|HLSLPROGRAM|CGINCLUDE|HLSLINCLUDE)\b.*?\b(ENDCG|ENDHLSL)\b", m =>
        {
            blocks.Add(m.Value);
            return " RU_BLOCK_" + (blocks.Count - 1) + " ";
        }, RegexOptions.Singleline);
        foreach (Match pass in Regex.Matches(masked, @"\bPass\s*\{").Cast<Match>().Reverse())
        {
            int end = pass.Index + pass.Length, depth = 1;
            bool quoted = false;
            while (end < masked.Length && depth > 0)
            {
                char c = masked[end++];
                if (c == '"') quoted = !quoted;
                if (!quoted) { if (c == '{') depth++; else if (c == '}') depth--; }
            }
            if (depth != 0) throw new InvalidDataException("Surface Pass 括号未闭合。");
            string body = masked.Substring(pass.Index, end - pass.Index);
            if (!Regex.IsMatch(body, @"""LightMode""\s*=\s*""ShadowCaster""", RegexOptions.IgnoreCase))
                throw new NotSupportedException("Unlit Surface 只支持附加 ShadowCaster；其他颜色 Pass 不能丢弃。");
            masked = masked.Remove(pass.Index, end - pass.Index);
        }
        string include = "";
        masked = Regex.Replace(masked, @"RU_BLOCK_(\d+)", m =>
        {
            string block = blocks[int.Parse(m.Groups[1].Value)];
            var shared = Regex.Match(block, @"^(?:CGINCLUDE|HLSLINCLUDE)(.*?)(?:ENDCG|ENDHLSL)$", RegexOptions.Singleline);
            if (!shared.Success) return m.Value;
            include += shared.Groups[1].Value + "\n";
            return "";
        });
        return Regex.Replace(masked, @"RU_BLOCK_(\d+)", m =>
            Regex.Replace(blocks[int.Parse(m.Groups[1].Value)], @"^(CGPROGRAM|HLSLPROGRAM)", p => p.Value + "\n" + include));
    }
}
