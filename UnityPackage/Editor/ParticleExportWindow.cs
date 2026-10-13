using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //保存当前工程的批量路径，后续只需点击一次导出按钮。
    public sealed class ParticleExportWindow : EditorWindow
    {
        private string folder, target, game, tool, message = "";
        private string Key => "RimUnity.Particles." + Application.dataPath + ".";

        [MenuItem("Tools/RimUnity/一键导出项目粒子")]
        private static void Open() => GetWindow<ParticleExportWindow>("RimUnity 粒子");

        //首次安装可定位随 UPM 提供的离线转换工具。
        private void OnEnable()
        {
            folder = EditorPrefs.GetString(Key + "Folder", "Assets");
            target = EditorPrefs.GetString(Key + "Target", "");
            game = EditorPrefs.GetString("RimUnity.GameRoot", "");
            tool = EditorPrefs.GetString("RimUnity.ShaderTool", "");
            if (!File.Exists(tool))
            {
                string candidate = Path.GetFullPath("Packages/com.chezhou.rimunity/Tools~/ShaderTool/RimUnity.ShaderTool.exe");
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ParticleExportWindow).Assembly);
                if (package != null) candidate = Path.Combine(package.resolvedPath, "Tools~/ShaderTool/RimUnity.ShaderTool.exe");
                if (File.Exists(candidate)) tool = candidate;
            }
        }

        //错误完整写入报告，窗口仅展示本轮汇总。
        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            folder = EditorGUILayout.TextField("资源目录", folder);
            target = EditorGUILayout.TextField("目标 Mod 目录", target);
            game = EditorGUILayout.TextField("RimWorld 安装目录", game);
            tool = EditorGUILayout.TextField("ShaderTool exe", tool);
            if (EditorGUI.EndChangeCheck()) Save();
            if (GUILayout.Button("选择目标 Mod")) { string selected = EditorUtility.OpenFolderPanel("选择含 About/About.xml 的 Mod", target, ""); if (selected.Length > 0) { target = selected; Save(); } }
            EditorGUILayout.HelpBox("展开目录内所有粒子 Prefab（含嵌套与 Variant）。成功项进入游戏清单，失败项保留旧产物并写入报告。导出通过仍需游戏验收。", MessageType.Info);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(target)))
                if (GUILayout.Button("一键导出所有粒子", GUILayout.Height(36)))
                    try
                    {
                        Save();
                        var results = ParticleBatchExporter.ExportFolder(target, folder, Path.Combine(target, "Docs", "ParticleExport-report.json"));
                        message = "成功 " + results.Count(r => r.exported) + "/" + results.Count + "；详情见目标 Mod 的 Docs/ParticleExport-report.json";
                    }
                    catch (Exception error) { message = error.Message; Debug.LogException(error); }
            EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
        }

        //本机路径留在编辑器配置，资源载荷不包含绝对路径。
        private void Save()
        {
            EditorPrefs.SetString(Key + "Folder", folder); EditorPrefs.SetString(Key + "Target", target);
            EditorPrefs.SetString("RimUnity.GameRoot", game); EditorPrefs.SetString("RimUnity.ShaderTool", tool);
        }
    }
}
