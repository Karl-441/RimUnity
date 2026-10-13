using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using Brutal.SlangApi;
using Brutal.Strings;

namespace RimUnity.ShaderTool;

//先由 Slang 展开实际变体，再收集材质声明，避免读取未启用分支的资源。
internal static class ShaderPreprocessor
{
    internal static string Run(string source)
    {
        string input = Path.Combine(Path.GetTempPath(), "RimUnity-" + System.Guid.NewGuid().ToString("N") + ".hlsl");
        try
        {
            File.WriteAllText(input, source, new UTF8Encoding(false));
            var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            start.ArgumentList.Add(Environment.GetCommandLineArgs()[1]); start.ArgumentList.Add("preprocess"); start.ArgumentList.Add(input);
            using var process = Process.Start(start);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidDataException("Shader 预处理失败：" + stderr.GetAwaiter().GetResult());
            return stdout.GetAwaiter().GetResult();
        }
        finally { File.Delete(input); }
    }

    //编译器的 -E 将展开结果写入标准输出，由上层单独捕获。
    internal static unsafe void Write(string file)
    {
        GlobalSessionDesc desc = new() { StructureSize = (uint)sizeof(GlobalSessionDesc), EnableGlsl = true };
        Slang.PInvoke.slang_createGlobalSession2(in desc, out nint pointer).ThrowIfFailure();
        var wrappers = new StrategyBasedComWrappers();
        IGlobalSession session;
        try { session = (IGlobalSession)wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None); }
        finally { Marshal.Release(pointer); }
        using var request = session.CreateCompileRequest();
        fixed (byte* option = Encoding.UTF8.GetBytes("-E\0"))
        {
            byte** options = stackalloc byte*[1]; options[0] = option;
            request.ProcessCommandLineArguments(options, 1).ThrowIfFailure();
        }
        int unit = request.AddTranslationUnit(SourceLanguage.Hlsl, new RefString8(Encoding.UTF8.GetBytes("source\0")));
        request.AddTranslationUnitSourceFile(unit, new RefString8(Encoding.UTF8.GetBytes(file + '\0')));
        if (request.Compile().IsFailure) throw new InvalidDataException(request.GetDiagnosticOutput());
    }
}
