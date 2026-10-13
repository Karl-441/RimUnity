using System;
using System.Collections.Generic;

namespace RimUnity.Serialization
{
    //预制体保存最终层级；嵌套和 Variant 在编辑器端展开。
    [Serializable]
    public sealed class PrefabData
    {
        public int formatVersion = 1;
        public List<NodeData> nodes = new List<NodeData>();
        public List<string> warnings = new List<string>();
    }

    //父节点 ID 为 null 时表示根节点。
    [Serializable]
    public sealed class NodeData
    {
        public string id, transformId, parent, name;
        public int layer;
        public bool active, rect;
        public float[] position, rotation, scale;
        public float[] anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition;
        public List<ComponentData> components = new List<ComponentData>();
    }

    //内置组件使用公开成员名，自定义脚本使用序列化字段名。
    [Serializable]
    public sealed class ComponentData
    {
        public string id, type;
        public bool enabled = true;
        public List<MemberData> members = new List<MemberData>();
    }

    [Serializable]
    public sealed class MemberData
    {
        public string name;
        public bool property;
        public ValueData value;
    }
}
