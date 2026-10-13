using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RimUnity.Editor
{
    //UTF-8 JSON 保留中文，可直接进入版本控制。
    internal static class ExportJson
    {
        internal static void Write(string file, object value)
            => File.WriteAllText(file, JsonConvert.SerializeObject(value, Formatting.Indented), new UTF8Encoding(false));
    }
}
