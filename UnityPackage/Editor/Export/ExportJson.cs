using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RimUnity.Editor
{
    //UTF-8 JSON 保留中文，可直接进入版本控制。
    internal static class ExportJson
    {
        //保留显式零值和 false；只省略空字段与排版，避免改变组件默认值语义。
        internal static void Write(string file, object value)
        {
            using var stream = new StreamWriter(file, false, new UTF8Encoding(false));
            using var json = new JsonTextWriter(stream);
            JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }).Serialize(json, value);
        }
    }
}
