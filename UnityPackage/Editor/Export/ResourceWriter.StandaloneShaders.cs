using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Editor
{
    //独立 Shader 复用材质的 Pass 选择，保留顺序、默认参数与各自的渲染状态。
    internal sealed partial class ResourceWriter
    {
        private string AddStandaloneShader(Material defaults, bool ui, bool cameraUi, string path)
        {
            var material = new ResourceData();
            var writer = new ValueWriter(new Dictionary<UnityEngine.Object, string>(), this);
            MaterialWriter.Write(defaults, material, writer, path, this, ui, cameraUi);
            var primary = material.members.First(m => m.name == "shader").value;
            if (primary.kind != "asset")
                throw new NotSupportedException(path + "：独立内置映射 Shader 请通过材质资源导出。");
            if (material.members.Any(m => m.name == "shadowCaster"))
                throw new NotSupportedException(path + "：带 ShadowCaster 的独立 Shader 请通过材质资源导出。");
            var passField = material.members.FirstOrDefault(m => m.name == "shaderPasses");
            string[] ids = passField == null ? new[] { primary.text } : passField.value.items.Select(p => p.text).ToArray();
            ShaderData data = JsonConvert.DeserializeObject<ShaderData>(File.ReadAllText(Path.Combine(directory, Path.GetFileName(records[primary.text].file))));
            data.defaults = ShaderDefaultsWriter.Read(defaults);
            data.additionalPasses = ids.Skip(1).ToArray();
            //默认值和 Pass 组合参与哈希，单独导出不会修改已被材质引用的主 Pass。
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data)))).Replace("-", "").ToLowerInvariant();
            string id = "RimUnity_Standalone_" + hash;
            if (dependencies.Count > 0) dependencies.Peek().Add(id);
            if (records.ContainsKey(id)) return id;
            data.key = id;
            string file = "s_" + CustomShaderWriter.CompactHash(hash) + ".json";
            ExportJson.Write(Path.Combine(directory, file), data);
            records.Add(id, new AssetRecord { id = id, name = defaults.shader.name,
                sourcePath = UnityEditor.AssetDatabase.GetAssetPath(defaults.shader), description = "独立 GLSL Shader",
                path = "__assets/" + id, file = relative + "/" + file, type = "Shader",
                dependencies = data.additionalPasses.ToList() });
            return id;
        }
    }
}
