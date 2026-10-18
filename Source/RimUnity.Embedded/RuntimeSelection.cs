using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace RimUnity.Embedded;

//独立版优先，兼容普通 DLL 的提前类型扫描；无独立版时选择最高兼容内置载荷。
internal static class RuntimeSelection
{
    private static readonly string[] Names = { "RimUnity.Core", "RimUnity", "RimUnity.Spine" };

    internal static void Initialize()
    {
        var mods = LoadedModManager.RunningModsListForReading;
        var participants = mods.Where(m => m.assemblies.loadedAssemblies.Any(a =>
            a.GetName().Name.StartsWith("RimUnity.Embedded.", StringComparison.Ordinal))).ToArray();
        var content = new DeferredContent(participants);
        var copies = new List<(string Path, ModContentPack Owner)>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in participants)
        foreach (string folder in mod.foldersToLoadDescendingOrder)
        {
            string directory = Path.Combine(folder, "Assemblies");
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.GetFiles(directory, "RimUnity.Embedded.dll", SearchOption.AllDirectories))
                if (paths.Add(file)) copies.Add((file, mod));
        }
        Assembly runtime;
        string root, provider, hash = "";
        bool standalone;
        bool deferred;
        //以原版登记的真实程序集归属识别运行时，不依赖管理器可改写的包 ID、名称或工坊链接。
        var runtimeOwners = mods.Where(m => m.assemblies.loadedAssemblies.Any(a => a.GetName().Name == "RimUnity")).ToArray();
        if (runtimeOwners.Length > 1)
            throw new InvalidOperationException("RimUnity runtime owner is ambiguous: " + string.Join(", ", runtimeOwners.Select(m => m.PackageId)));
        var standaloneMod = runtimeOwners.SingleOrDefault();
        if (standaloneMod != null)
        {
            //独立版已由原版加载，必须共用其类型，不能再加载或替换第二套运行时。
            runtime = SelectOwnedRuntime(standaloneMod, standaloneMod.assemblies.loadedAssemblies);
            string protocol = runtime.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RimUnityProtocol")?.Value;
            var version = Version.Parse(runtime.GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
            if (protocol != "1" || version < content.Minimum)
                throw new InvalidOperationException($"独立 RimUnity 版本不兼容，请更新独立版。/ Update standalone RimUnity. Required: {content.Minimum}, installed: {version}");
            VerifyLoadedRuntime(standaloneMod.assemblies.loadedAssemblies);
            root = FindRuntimeResources(standaloneMod, runtime);
            provider = standaloneMod.Name;
            standalone = true;
            deferred = false;
        }
        else
        {
            var unique = new Dictionary<string, RuntimePayload>(StringComparer.Ordinal);
            var payloads = new List<(RuntimePayload Payload, ModContentPack Owner)>();
            foreach (var copy in copies)
            {
                var payload = RuntimePayload.Read(copy.Path);
                if (!unique.TryGetValue(payload.Hash, out var shared)) unique.Add(payload.Hash, shared = payload);
                payloads.Add((shared, copy.Owner));
            }
            foreach (var group in payloads.GroupBy(c => c.Payload.Version))
                if (group.Select(c => c.Payload.Hash).Distinct().Count() != 1)
                    throw new InvalidDataException("相同版本运行库载荷冲突 / Conflicting embedded payloads: " + group.Key);
            var compatible = payloads.Where(c => c.Payload.Protocol == 1 && c.Payload.Version >= content.Minimum)
                .OrderByDescending(c => c.Payload.Version)
                .ThenBy(c => c.Owner.PackageId, StringComparer.Ordinal).ToArray();
            if (compatible.Length == 0) throw new InvalidOperationException("没有兼容运行库 / No compatible embedded runtime: " + content.Minimum);
            var selected = compatible[0];
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => Names.Contains(a.GetName().Name)))
                throw new InvalidOperationException("检测到提前加载的 RimUnity DLL，请勿将 References 放入模组。/ Runtime DLLs loaded early; do not deploy References.");
            root = selected.Payload.Extract(Path.Combine(GenFilePaths.SaveDataFolderPath, "RimUnity", "Embedded"));
            hash = selected.Payload.Hash;
            provider = selected.Owner.Name;
            standalone = false;
            deferred = true;
            foreach (string name in Names)
                selected.Owner.assemblies.loadedAssemblies.Add(Assembly.LoadFrom(Path.Combine(root, "Assemblies", name + ".dll")));
            runtime = SelectOwnedRuntime(selected.Owner, selected.Owner.assemblies.loadedAssemblies);
            selected.Owner.foldersToLoadDescendingOrder.Add(root);
            UnityStubs.ContentPacks.MountLoadFolder(root);
            ShaderDefRegistry.RegisterShaderDirectories();
            VerifyLoadedRuntime(selected.Owner.assemblies.loadedAssemblies);
        }
        runtime.GetType("RimUnity.RuntimeInfo", true).GetMethod("Configure").Invoke(null,
            new object[] { root, standalone, provider, copies.Count, hash });
        if (deferred)
        {
            GenTypes.ClearCache();
            LoadedModManager.CreateModClasses();
        }
        if (!LoadedModManager.ModHandles.Any(m => m.GetType().Assembly == runtime && m.GetType().FullName == "RimUnity.RimUnityMod"))
            throw new InvalidOperationException("运行时初始化失败，停止加载内容脚本。/ Runtime initialization failed; content scripts were not loaded.");
        if (content.Files.Count != 0)
        {
            content.Load();
            GenTypes.ClearCache();
            LoadedModManager.CreateModClasses();
        }
        Log.Message($"[RimUnity] {(standalone ? "Standalone" : "Embedded")} provider: {provider}; version: {runtime.GetCustomAttribute<AssemblyFileVersionAttribute>().Version}; candidates: {copies.Count}; payload: {hash}");
    }

    //资源必须来自该运行时的有效加载目录；散装 References 不能冒充完整独立包。
    private static string FindRuntimeResources(ModContentPack owner, Assembly runtime)
    {
        string location = Path.GetFullPath(runtime.Location);
        string root = owner.foldersToLoadDescendingOrder.OrderByDescending(f => f.Length).FirstOrDefault(folder =>
            location.StartsWith(Path.GetFullPath(Path.Combine(folder, "Assemblies")) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
        if (root == null || !File.Exists(Path.Combine(root, "Defs", "RimUnityShaders.xml")) ||
            !Directory.Exists(Path.Combine(root, "Shaders", "RimUnity")))
            throw new InvalidDataException("RimUnity runtime resources are missing from its active load folder: " + owner.RootDir);
        return root;
    }

    private static void VerifyLoadedRuntime(IEnumerable<Assembly> owned)
    {
        foreach (string name in Names)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == name).ToArray();
            if (matches.Length != 1 || !owned.Contains(matches[0]))
                throw new InvalidOperationException("RimUnity assembly ownership conflict: " + name);
        }
    }

    //取该模组名下唯一的 RimUnity 程序集；同名程序集多于一个时给双语错误并列出文件。
    //原版不保证一个模组里同名程序集唯一（Assemblies/**/*.dll 递归加载、不去重），
    //直接 Single 会抛裸 InvalidOperationException，日志里看不出是哪个文件冲突（issue #7）。
    private static Assembly SelectOwnedRuntime(ModContentPack owner, IEnumerable<Assembly> owned)
    {
        var matches = owned.Where(a => a.GetName().Name == "RimUnity").ToArray();
        if (matches.Length == 1) return matches[0];
        string files = matches.Length == 0
            ? "（没有找到）/ none found"
            : string.Join(", ", matches.Select(a => a.Location).Distinct(StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException(
            $"模组 {owner.PackageId} 里名为 RimUnity 的程序集登记了 {matches.Length} 次（文件：{files}）。请删除重复或残留的 DLL 后重启游戏。"
            + $" / Mod {owner.PackageId} registered {matches.Length} assemblies named RimUnity (files: {files}). Remove the duplicate or stale DLL and restart.");
    }
}
