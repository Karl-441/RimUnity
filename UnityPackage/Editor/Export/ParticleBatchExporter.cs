using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //逐个提交完整预制体，失败项保留定位信息，不阻断其他粒子的导出。
    public static class ParticleBatchExporter
    {
        //Unity 负责展开嵌套预制体和 Variant，逻辑路径保留目录以避免同名覆盖。
        public static List<ParticleExportResult> ExportFolder(string mod, string folder, string report, string[] selected = null)
        {
            if (!AssetDatabase.IsValidFolder(folder)) throw new DirectoryNotFoundException("资源目录不存在：" + folder);
            var results = new List<ParticleExportResult>();
            var warnings = new List<string>();
            void Capture(string message, string trace, LogType type) { if (type == LogType.Warning) warnings.Add(message); }
            Application.logMessageReceived += Capture;
            string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (selected != null)
            {
                var set = new HashSet<string>(selected, StringComparer.Ordinal);
                paths = paths.Where(set.Contains).ToArray();
                if (paths.Length != set.Count) throw new FileNotFoundException("选定的部分粒子 Prefab 未找到。");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));
            try
            {
                for (int index = 0; index < paths.Length; index++)
                {
                    string path = paths[index];
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) throw new InvalidDataException("Prefab 未能导入：" + path);
                    var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                    if (systems.Length == 0) continue;
                    if (!Application.isBatchMode) EditorUtility.DisplayProgressBar("RimUnity 粒子导出", path, (float)index / paths.Length);
                    warnings.Clear();
                    var result = new ParticleExportResult { prefab = path, particleSystems = systems.Length,
                        components = prefab.GetComponentsInChildren<Component>(true).Select(c => c == null ? "MissingScript" : c.GetType().FullName).Distinct().ToArray(),
                        shaders = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader.name).Distinct().ToArray() };
                    try
                    {
                        PrefabExporter.Export(mod, new ExportRequest { prefab = prefab, path = "Particles/" + path.Substring(7, path.Length - 14), description = "源预制体：" + path });
                        result.exported = true;
                        result.degraded = result.components.Contains("MissingScript");
                    }
                    catch (Exception error) { result.error = error.ToString(); Debug.LogError("[RimUnity] 未导出 " + path + "\n" + error); }
                    result.warnings = warnings.ToArray();
                    results.Add(result);
                    File.WriteAllText(report, JsonConvert.SerializeObject(results, Formatting.Indented), new System.Text.UTF8Encoding(false));
                }
                Debug.Log("[RimUnity] 粒子批次结束：成功 " + results.Count(r => r.exported) + "/" + results.Count + "。报告：" + report);
                return results;
            }
            finally { Application.logMessageReceived -= Capture; if (!Application.isBatchMode) EditorUtility.ClearProgressBar(); }
        }

        //命令行和编辑器窗口使用同一导出流程。
        public static void Batch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Argument(string name) { int i = Array.IndexOf(args, name); if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("缺少参数：" + name); return args[i + 1]; }
            string oldTool = EditorPrefs.GetString("RimUnity.ShaderTool", ""), oldGame = EditorPrefs.GetString("RimUnity.GameRoot", "");
            try
            {
                EditorPrefs.SetString("RimUnity.ShaderTool", Argument("-rimUnityShaderTool"));
                EditorPrefs.SetString("RimUnity.GameRoot", Argument("-rimUnityGame"));
                string[] selected = args.Contains("-rimUnityPrefabList") ? JsonConvert.DeserializeObject<string[]>(File.ReadAllText(Argument("-rimUnityPrefabList"))) : null;
                ExportFolder(Argument("-rimUnityMod"), Argument("-rimUnityFolder"), Argument("-rimUnityReport"), selected);
            }
            finally { EditorPrefs.SetString("RimUnity.ShaderTool", oldTool); EditorPrefs.SetString("RimUnity.GameRoot", oldGame); }
        }
    }

    //导出状态与游戏验收分离，失败项不能被算作可播放资源。
    public sealed class ParticleExportResult
    {
        public string prefab, error;
        public bool exported, degraded;
        public int particleSystems;
        public string[] components, shaders, warnings;
    }
}
