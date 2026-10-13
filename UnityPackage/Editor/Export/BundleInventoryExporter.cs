using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //使用 Unity 实际的包归属及依赖解析，包含目录继承的包标签。
    public static class BundleInventoryExporter
    {
        //可与一次选定粒子重导共用编辑器启动，避免反复启动专用工程。
        public static void Batch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-rimUnityBundleReport");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("缺少 -rimUnityBundleReport。");
            Write(args[index + 1]);
            if (args.Contains("-rimUnityPrefabList")) ParticleBatchExporter.Batch();
        }

        //仅读取导入后的资源，不生成 AB，也不调用原工程的部署脚本。
        public static void Write(string file)
        {
            var bundles = new List<BundleEntry>();
            var all = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in AssetDatabase.GetAllAssetBundleNames().OrderBy(n => n, StringComparer.Ordinal))
            {
                string[] roots = AssetDatabase.GetAssetPathsFromAssetBundle(name).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                if (roots.Length == 0) continue;
                string[] dependencies = AssetDatabase.GetDependencies(roots, true).Where(p => !AssetDatabase.IsValidFolder(p)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                bundles.Add(new BundleEntry { name = name, entries = roots, dependencies = dependencies });
                all.UnionWith(dependencies); all.UnionWith(roots.Where(p => !AssetDatabase.IsValidFolder(p)));
            }
            var assets = new List<AssetEntry>();
            var materials = new List<MaterialEntry>();
            foreach (string path in all.OrderBy(p => p, StringComparer.Ordinal))
            {
                var entry = new AssetEntry { path = path, bundle = AssetDatabase.GetImplicitAssetBundleName(path),
                    type = AssetDatabase.GetMainAssetTypeAtPath(path)?.FullName ?? "MissingAsset" };
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) { entry.type = "MissingPrefab"; }
                    else
                    {
                        entry.components = prefab.GetComponentsInChildren<Component>(true).Select(c => c == null ? "MissingScript" : c.GetType().FullName).Distinct().OrderBy(t => t).ToArray();
                        entry.particleSystems = prefab.GetComponentsInChildren<ParticleSystem>(true).Length;
                    }
                }
                assets.Add(entry);
                foreach (Material material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId);
                    materials.Add(new MaterialEntry { path = path, name = material.name, id = guid + "_" + localId,
                        shader = material.shader == null ? null : material.shader.name,
                        shaderPath = material.shader == null ? null : AssetDatabase.GetAssetPath(material.shader),
                        keywords = material.shaderKeywords.OrderBy(k => k).ToArray(), passes = material.passCount, renderQueue = material.renderQueue });
                }
            }
            var report = new BundleReport { unityVersion = Application.unityVersion, bundles = bundles, assets = assets, materials = materials };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            File.WriteAllText(file, JsonConvert.SerializeObject(report, Formatting.Indented), new System.Text.UTF8Encoding(false));
            Debug.Log("[RimUnity] AB 适配范围：" + bundles.Count + " 个包，" + assets.Count + " 个入口／依赖路径，" + materials.Count + " 个材质。清单：" + file);
        }

        //依赖中的 MonoScript 是类型引用，不表示 AB 携带可执行脚本程序集。
        [Serializable] private sealed class BundleReport
        {
            public string unityVersion;
            public List<BundleEntry> bundles;
            public List<AssetEntry> assets;
            public List<MaterialEntry> materials;
        }
        [Serializable] private sealed class BundleEntry { public string name; public string[] entries, dependencies; }
        [Serializable] private sealed class AssetEntry
        {
            public string path, bundle, type;
            public string[] components = Array.Empty<string>();
            public int particleSystems;
        }
        [Serializable] private sealed class MaterialEntry
        {
            public string path, name, id, shader, shaderPath;
            public string[] keywords;
            public int passes, renderQueue;
        }
    }
}
