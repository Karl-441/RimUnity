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
    private static bool IsStandalone(ModContentPack mod) => string.Equals(mod.PackageId, "Chezhou.RimUnity", StringComparison.OrdinalIgnoreCase);

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
        var standaloneMod = mods.SingleOrDefault(m => IsStandalone(m) && m.assemblies.loadedAssemblies.Any(a => a.GetName().Name == "RimUnity"));
        if (standaloneMod != null)
        {
            //独立版已由原版加载，必须共用其类型，不能再加载或替换第二套运行时。
            runtime = standaloneMod.assemblies.loadedAssemblies.Single(a => a.GetName().Name == "RimUnity");
            string protocol = runtime.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RimUnityProtocol")?.Value;
            var version = Version.Parse(runtime.GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
            if (protocol != "1" || version < content.Minimum)
                throw new InvalidOperationException($"独立 RimUnity 版本不兼容，请更新独立版。/ Update standalone RimUnity. Required: {content.Minimum}, installed: {version}");
            root = Path.Combine(standaloneMod.RootDir, "1.7");
            provider = standaloneMod.Name;
            standalone = true;
            deferred = false;
            VerifyLoadedRuntime(standaloneMod.assemblies.loadedAssemblies);
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
                .OrderByDescending(c => c.Payload.Version).ThenByDescending(c => IsStandalone(c.Owner))
                .ThenBy(c => c.Owner.PackageId, StringComparer.Ordinal).ToArray();
            if (compatible.Length == 0) throw new InvalidOperationException("没有兼容运行库 / No compatible embedded runtime: " + content.Minimum);
            var selected = compatible[0];
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => Names.Contains(a.GetName().Name)))
                throw new InvalidOperationException("检测到提前加载的 RimUnity DLL，请勿将 References 放入模组。/ Runtime DLLs loaded early; do not deploy References.");
            root = selected.Payload.Extract(Path.Combine(GenFilePaths.SaveDataFolderPath, "RimUnity", "Embedded"));
            hash = selected.Payload.Hash;
            provider = selected.Owner.Name;
            standalone = IsStandalone(selected.Owner);
            deferred = true;
            foreach (string name in Names)
                selected.Owner.assemblies.loadedAssemblies.Add(Assembly.LoadFrom(Path.Combine(root, "Assemblies", name + ".dll")));
            runtime = selected.Owner.assemblies.loadedAssemblies.Single(a => a.GetName().Name == "RimUnity");
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

    private static void VerifyLoadedRuntime(IEnumerable<Assembly> owned)
    {
        foreach (string name in Names)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == name).ToArray();
            if (matches.Length != 1 || !owned.Contains(matches[0]))
                throw new InvalidOperationException("RimUnity assembly ownership conflict: " + name);
        }
    }
}
