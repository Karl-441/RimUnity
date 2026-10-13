using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//在调用引擎 SetParent 前拒绝重复 ID、断开的层级及环。
internal static class PrefabStructure
{
    internal static void Validate(PrefabData data)
    {
        if (data.formatVersion != 1 || data.nodes.Count == 0) throw new InvalidDataException("无效的 RimUnity Prefab 载荷。");
        var nodes = data.nodes.ToDictionary(n => n.id, StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        NodeData[] roots = data.nodes.Where(n => n.parent == null).ToArray();
        if (roots.Length != 1) throw new InvalidDataException("预制体必须只有一个根节点。");
        void Add(string id) { if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw new InvalidDataException("无效或重复的对象 ID：" + id); }
        foreach (NodeData node in data.nodes)
        {
            Add(node.id); Add(node.transformId);
            foreach (ComponentData component in node.components) Add(component.id);
            Check(node.position, 3, node.name); Check(node.rotation, 4, node.name); Check(node.scale, 3, node.name);
            if (node.rect)
            {
                Check(node.anchorMin, 2, node.name); Check(node.anchorMax, 2, node.name);
                Check(node.pivot, 2, node.name); Check(node.sizeDelta, 2, node.name); Check(node.anchoredPosition, 2, node.name);
            }
            if (node.parent == null) continue;
            if (!nodes.ContainsKey(node.parent)) throw new InvalidDataException(node.name + "：父节点不存在。");
            if (!children.TryGetValue(node.parent, out var list)) children.Add(node.parent, list = new());
            list.Add(node.id);
        }
        var stack = new Stack<string>(); stack.Push(roots[0].id);
        int visited = 0;
        while (stack.Count > 0)
        {
            string id = stack.Pop(); visited++;
            if (children.TryGetValue(id, out var list)) foreach (string child in list) stack.Push(child);
        }
        if (visited != nodes.Count) throw new InvalidDataException("预制体层级包含环或未连接的子树。");
    }

    //数值长度错误不能进入引擎矩阵计算。
    private static void Check(float[] value, int count, string node)
    {
        if (value == null || value.Length != count || value.Any(v => !float.IsFinite(v))) throw new InvalidDataException(node + "：变换分量无效。");
    }
}
