using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RimUnity.ShaderTool;

//ShaderLab 词法只解析容器与状态，程序块在进入这里前已隔离。
internal sealed class ShaderLabTokens
{
    private readonly List<string> tokens = new();
    private int index;
    internal bool End => index >= tokens.Count;
    internal string Peek => End ? "" : tokens[index];

    //引号内的标点和空白保持为一个词。
    internal ShaderLabTokens(string source)
    {
        for (int i = 0; i < source.Length;)
        {
            if (char.IsWhiteSpace(source[i])) { i++; continue; }
            int start = i;
            if (source[i] == '"')
            {
                i++;
                bool closed = false;
                while (i < source.Length)
                {
                    if (source[i] == '\\') { i += 2; continue; }
                    if (source[i++] == '"') { closed = true; break; }
                }
                if (!closed) throw new InvalidDataException("ShaderLab 字符串没有闭合。");
            }
            else if ("{}()[],=".Contains(source[i])) i++;
            else while (i < source.Length && !char.IsWhiteSpace(source[i]) && !"{}()[],=\"".Contains(source[i])) i++;
            tokens.Add(source.Substring(start, i - start));
        }
    }

    //消耗并返回下一个词，缺失输入不会用默认值代替。
    internal string Read() => !End ? tokens[index++] : throw new InvalidDataException("ShaderLab 意外结束。");
    //匹配标点或大小写不敏感的 ShaderLab 关键字。
    internal bool Take(string token)
    {
        if (!string.Equals(Peek, token, StringComparison.OrdinalIgnoreCase)) return false;
        index++; return true;
    }
    //固定语法不匹配时给出实际位置附近的词。
    internal void Expect(string token)
    {
        if (!Take(token)) throw new InvalidDataException($"ShaderLab 需要 '{token}'，实际为 '{Peek}'。");
    }
    //ShaderLab 名称和标签必须使用字符串字面量。
    internal string String()
    {
        string value = Read();
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"') throw new InvalidDataException("需要 ShaderLab 字符串：" + value);
        return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
    //取出平衡括号中的词，供属性类型与属性特性解析。
    internal string Balanced(string open, string close)
    {
        Expect(open);
        int depth = 1;
        List<string> content = new();
        while (depth > 0)
        {
            string token = Read();
            if (token == open) depth++;
            if (token == close) depth--;
            if (depth > 0) content.Add(token);
        }
        return string.Join(" ", content);
    }

    //移除注释但保留换行和字符串，避免注释中的 Pass/括号影响解析。
    internal static string WithoutComments(string source)
    {
        StringBuilder result = new(source.Length);
        bool quoted = false;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (quoted)
            {
                result.Append(c);
                if (c == '\\' && i + 1 < source.Length) result.Append(source[++i]);
                else if (c == '"') quoted = false;
            }
            else if (c == '"') { quoted = true; result.Append(c); }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] != '\n') i++;
                result.Append('\n');
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                { result.Append(source[i] == '\n' ? '\n' : ' '); i++; }
                if (i + 1 >= source.Length) throw new InvalidDataException("ShaderLab 块注释没有闭合。");
                result.Append(' '); i++;
            }
            else result.Append(c);
        }
        return result.ToString();
    }
}
