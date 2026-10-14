using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimUnity.Editor
{
    //按清单复制发布内容，附属文件按内容共享，不合并不同资源的对象身份。
    public static class ExportStorage
    {
        //目标必须为空目录；源目录保持不变，失败由发布事务处理。
        public static void CopyForPublish(string source, string destination)
        {
            source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase)
                || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("资源源目录与发布目录不能相同或互相包含。");
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new IOException("资源整理目标必须为空：" + destination);
            Directory.CreateDirectory(destination);
            var catalog = Read(Inside(source, "catalog.json"));
            var assets = (JArray)catalog["assets"];
            if ((int)catalog["formatVersion"] != 1 || assets == null)
                throw new InvalidDataException("资源清单格式无效。");
            var records = assets.Cast<JObject>().ToDictionary(a => (string)a["id"], StringComparer.Ordinal);
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string id)
            {
                if (!records.TryGetValue(id, out var record)) throw new InvalidDataException("缺少依赖：" + id);
                if (!reachable.Add(id)) return;
                foreach (string dependency in ((JArray)record["dependencies"]).Values<string>()) Visit(dependency);
            }
            foreach (var record in records.Values)
                if (!((string)record["path"]).StartsWith("__assets/", StringComparison.Ordinal)) Visit((string)record["id"]);
            var payloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string CopyPayload(string relative)
            {
                string file = Inside(source, relative);
                if (payloads.TryGetValue(file, out var cached)) return cached;
                string hash;
                using (var stream = File.OpenRead(file))
                using (var algorithm = SHA256.Create())
                    hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                string target = "blobs/" + hash + Path.GetExtension(file).ToLowerInvariant();
                string output = Inside(destination, target);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                if (!File.Exists(output)) File.Copy(file, output);
                payloads.Add(file, target);
                return target;
            }
            var kept = new JArray();
            foreach (var record in records.Values.Where(a => reachable.Contains((string)a["id"])))
            {
                string relative = (string)record["file"];
                var data = Read(Inside(source, relative));
                foreach (string field in new[] { "vertexFile", "fragmentFile" })
                    if (data[field]?.Type == JTokenType.String) data[field] = CopyPayload((string)data[field]);
                if (data["descriptorFile"]?.Type == JTokenType.String)
                {
                    var descriptor = Read(Inside(source, (string)data["descriptorFile"]));
                    //离线描述中的阶段路径相对于描述文件，随共享程序一起更新。
                    descriptor["vertexFile"] = Path.GetFileName((string)data["vertexFile"]);
                    descriptor["fragmentFile"] = Path.GetFileName((string)data["fragmentFile"]);
                    byte[] bytes = Encoding.UTF8.GetBytes(descriptor.ToString(Formatting.None));
                    string hash;
                    using (var algorithm = SHA256.Create())
                        hash = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    string target = "blobs/" + hash + ".json";
                    string descriptorOutput = Inside(destination, target);
                    Directory.CreateDirectory(Path.GetDirectoryName(descriptorOutput));
                    if (!File.Exists(descriptorOutput)) File.WriteAllBytes(descriptorOutput, bytes);
                    data["descriptorFile"] = target;
                }
                if (data["members"] is JArray members)
                    foreach (var member in members.OfType<JObject>())
                        if ((string)member["name"] == "file" && member["value"] is JObject value)
                            value["text"] = CopyPayload((string)value["text"]);
                //只移除 null，不移除 0、false 或空集合。
                foreach (var property in data.Descendants().OfType<JProperty>().Where(p => p.Value.Type == JTokenType.Null).ToArray())
                    property.Remove();
                string output = Inside(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                ExportJson.Write(output, data);
                kept.Add(record);
            }
            catalog["assets"] = kept;
            ExportJson.Write(Path.Combine(destination, "catalog.json"), catalog);
        }

        //逐文件读取，避免将整个资源目录同时留在内存中。
        private static JObject Read(string path)
        {
            using var stream = File.OpenText(path);
            using var reader = new JsonTextReader(stream) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        //引用只能落在本资源根目录，拒绝通过链接跳出目录。
        private static string Inside(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("无效资源路径：" + relative);
            string full = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("资源路径越界：" + relative);
            for (string path = full; path != null && path.Length >= root.Length; path = Path.GetDirectoryName(path))
                if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("资源路径不可包含链接：" + path);
            return full;
        }
    }
}
