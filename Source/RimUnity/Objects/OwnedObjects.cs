using System.Runtime.CompilerServices;
using UnityEngine;

namespace RimUnity;

//补丁只接管本桥接层创建的对象，避免改变其它模组的对象语义。
internal static class OwnedObjects
{
    private sealed class Marker { internal bool Template; }
    private static readonly ConditionalWeakTable<GameObject, Marker> Objects = new();
    internal static void Register(GameObject go) => Objects.Add(go, new Marker());
    internal static bool Contains(GameObject go) => go != null && Objects.TryGetValue(go, out _);
    internal static void MarkTemplate(GameObject go) => Objects.GetValue(go, _ => new Marker()).Template = true;
    internal static bool IsTemplate(GameObject go) => go != null && Objects.TryGetValue(go, out Marker marker) && marker.Template;
}
