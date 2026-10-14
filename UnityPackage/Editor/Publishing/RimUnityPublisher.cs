using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using RimUnity.Editor;
using UnityEditor;
using UnityEngine;

// A content snapshot is prepared in isolation. No live file is deleted until every
// target has exported and validated. Publication rollback restores the prior snapshot.
namespace RimUnity.Editor { public static class RimUnityPublisher
{
    private sealed class Target
    {
        public string Mod, Live, Stage, StageParent, Prepared, Backup;
        public bool HadOld, Published;
        public FileStream Lock;
        public RimUnityWorkflow.Entry[] Entries;
    }

    public static void Export(RimUnityWorkflow.Profile profile,
        IEnumerable<RimUnityWorkflow.Entry> selection, bool rebuild, Action<string> log)
    {
        var entries = selection.ToArray();
        if (entries.Length == 0) throw new InvalidOperationException("请先选择资源。");
        string blockers = DescribeBlockers(entries);
        if (blockers != null) throw new InvalidOperationException(blockers);
        if (!File.Exists(profile.shaderTool) || !File.Exists(Path.Combine(profile.gameRoot, "Waylith.dll")))
            throw new InvalidOperationException("ShaderTool 或 RimWorld 1.7 路径无效。");
        var targets = entries.GroupBy(e => Path.GetFullPath(Path.Combine(profile.repository, e.mod)), StringComparer.OrdinalIgnoreCase)
            .Select(group => new Target { Mod = group.Key, Entries = group.ToArray() }).ToArray();
        string oldTool = EditorPrefs.GetString("RimUnity.ShaderTool", ""), oldGame = EditorPrefs.GetString("RimUnity.GameRoot", "");
        bool committed = false;
        try
        {
            foreach (var target in targets)
            {
                string about = Path.Combine(target.Mod, "About/About.xml");
                if (!File.Exists(about) || string.IsNullOrWhiteSpace(XDocument.Load(about).Root.Element("packageId")?.Value))
                    throw new InvalidOperationException("目标必须是有效内容 Mod 根目录：" + target.Mod);
                target.Live = Path.GetFullPath(Path.Combine(target.Mod, "1.7/Data/RimUnity"));
                string data = Path.GetDirectoryName(target.Live);
                Directory.CreateDirectory(data);
                EnsureNoLinks(data);
                target.Lock = new FileStream(Path.Combine(data, ".rimunity-publish.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                // Prevent publication while the framework's direct exporter is committing.
                string exportLock = Path.Combine(target.Live, ".export.lock");
                if (File.Exists(exportLock)) using (File.Open(exportLock, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                string id = Guid.NewGuid().ToString("N");
                string shortParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RimUnity"));
                target.StageParent = string.Equals(Path.GetPathRoot(shortParent), Path.GetPathRoot(data), StringComparison.OrdinalIgnoreCase) ? shortParent : data;
                Directory.CreateDirectory(target.StageParent);
                target.Stage = Path.Combine(target.StageParent, ".rimunity-stage-" + id);
                target.Backup = Path.Combine(data, ".rimunity-old-" + id);
                target.Prepared = Path.Combine(target.Stage, "1.7/Data/RimUnity");
                Directory.CreateDirectory(Path.Combine(target.Stage, "About"));
                File.Copy(about, Path.Combine(target.Stage, "About/About.xml"));
                if (!rebuild && Directory.Exists(target.Live)) CopyTree(target.Live, target.Prepared);
            }
            EditorPrefs.SetString("RimUnity.ShaderTool", profile.shaderTool);
            EditorPrefs.SetString("RimUnity.GameRoot", profile.gameRoot);
            int finished = 0;
            foreach (var target in targets)
            {
                foreach (var entry in target.Entries)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("RimUnity 明文导出", entry.path, (float)finished / entries.Length))
                        throw new OperationCanceledException("已取消；正式数据未替换。");
                    log("导出：" + entry.path);
                    PrefabExporter.Export(target.Stage, new ExportRequest {
                        asset = AssetDatabase.LoadMainAssetAtPath(entry.path), ui = entry.ui,
                        path = "Bundles/" + entry.bundle + "/" + entry.path, description = "源资源：" + entry.path });
                    finished++;
                }
                Validate(target.Prepared);
                log("验证通过：" + target.Mod);
            }
            // No cancellation after publication starts. If any rename fails, roll back
            // every published target before reporting failure (all backups still exist).
            foreach (var target in targets)
            {
                target.HadOld = Directory.Exists(target.Live);
                if (target.HadOld) Directory.Move(target.Live, target.Backup);
                try { Directory.Move(target.Prepared, target.Live); target.Published = true; }
                catch { if (target.HadOld) Directory.Move(target.Backup, target.Live); target.HadOld = false; throw; }
                log("已发布：" + target.Live);
            }
            committed = true;
            bool cleaned = true;
            foreach (var target in targets)
                if (target.HadOld) cleaned &= TryDeleteOwned(target.Backup, Path.GetDirectoryName(target.Live), ".rimunity-old-", log);
            log(rebuild ? "完整重建已发布；新清单只包含所选入口及依赖。" : "增量更新已发布；旧入口及其数据保留。");
            log(cleaned ? "替换前的旧目录已清理。" : "仍有旧版本目录未清理，具体路径见记录。");
        }
        catch
        {
            if (!committed)
                foreach (var target in targets.Reverse())
                    if (target.Published)
                    {
                        // Move the unpublished new snapshot back into our own stage rather
                        // than deleting live content during rollback.
                        Directory.Move(target.Live, target.Prepared);
                        if (target.HadOld) Directory.Move(target.Backup, target.Live);
                        target.Published = false;
                    }
            throw;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            EditorPrefs.SetString("RimUnity.ShaderTool", oldTool);
            EditorPrefs.SetString("RimUnity.GameRoot", oldGame);
            foreach (var target in targets)
            {
                target.Lock?.Dispose();
                if (target.Stage != null) TryDeleteOwned(target.Stage, target.StageParent, ".rimunity-stage-", log);
            }
        }
    }

    public static string DescribeBlockers(IEnumerable<RimUnityWorkflow.Entry> selection)
    {
        var blocked = selection.Where(e => e.status != "pending").ToArray();
        if (blocked.Length == 0) return null;
        return "以下 " + blocked.Length + " 个所选入口未通过检查，正式数据未替换：\n" +
            string.Join("\n", blocked.Select(e => e.path + "\n  原因：" + (string.IsNullOrWhiteSpace(e.error) ? e.status : e.error))) +
            "\n请修复这些入口，或明确取消选择后再导出。";
    }

    public static void Validate(string root)
    {
        root = Path.GetFullPath(root);
        var catalog = JObject.Parse(File.ReadAllText(Path.Combine(root, "catalog.json")));
        var assets = (JArray)catalog["assets"];
        if ((int)catalog["formatVersion"] != 1 || assets == null || assets.Count == 0) throw new InvalidDataException("导出清单为空或格式无效。");
        var ids = new HashSet<string>();
        foreach (JObject asset in assets)
            if (!ids.Add((string)asset["id"])) throw new InvalidDataException("重复资源 ID。");
        foreach (JObject asset in assets)
        {
            string file = Inside(root, (string)asset["file"]);
            if (!File.Exists(file)) throw new FileNotFoundException("清单资源不存在", file);
            foreach (string dependency in (asset["dependencies"] as JArray ?? new JArray()).Values<string>())
                if (!ids.Contains(dependency)) throw new InvalidDataException("缺失依赖：" + dependency);
            var json = JObject.Parse(File.ReadAllText(file));
            // Shader paths and ResourceData file members are content-root relative,
            // exactly as ResourceWriter emits them, rather than JSON-relative paths.
            foreach (string name in new[] { "vertexFile", "fragmentFile", "descriptorFile" })
                if (json[name]?.Type == JTokenType.String) CheckFile(root, (string)json[name]);
            if (json["members"] is JArray members)
                foreach (JObject member in members)
                    if ((string)member["name"] == "file") CheckFile(root, (string)member["value"]?["text"]);
            if (((string)asset["description"] ?? "").Contains("[降级导出]"))
                throw new InvalidDataException("资源存在降级警告，未发布：" + (string)asset["path"]);
        }
    }
    private static void CheckFile(string root, string relative)
    {
        string file = Inside(root, relative);
        if (!File.Exists(file)) throw new FileNotFoundException("缺失资源附属文件", file);
    }

    private static string Inside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("无效资源路径。");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("资源路径越界。");
        return full;
    }
    private static void EnsureNoLinks(string root)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("数据目录不可为链接：" + root);
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("数据目录包含链接：" + entry);
            if ((attributes & FileAttributes.Directory) != 0) EnsureNoLinks(entry);
        }
    }
    private static void CopyTree(string source, string target)
    {
        EnsureNoLinks(source);
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source)) CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
    private static bool TryDeleteOwned(string path, string parent, string prefix, Action<string> log)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new IOException("拒绝清理目标 Mod 数据目录之外的路径：" + full);
            string name = Path.GetFileName(full);
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring(prefix.Length), "N", out _))
                throw new IOException("拒绝清理非本次导出目录：" + full);
            if (!Directory.Exists(full)) return true;
            EnsureNoLinks(full);
            Directory.Delete(full, true);
            return true;
        }
        catch (Exception error) { log("清理未完成，请检查目录：" + path + "\n" + error.Message); return false; }
    }
}

}
