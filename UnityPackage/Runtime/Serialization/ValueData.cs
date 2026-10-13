using System;
using System.Collections.Generic;

namespace RimUnity.Serialization
{
    //显式区分资源引用、实例引用与值，避免把编辑器实例 ID 写入产物。
    [Serializable]
    public sealed class ValueData
    {
        public string kind, type, text;
        public double number;
        public float[] vector;
        public List<ValueData> items;
        public List<MemberData> fields;
        public List<EventCallData> calls;
    }

    //事件模式沿用 PersistentListenerMode，动态参数由事件签名确定。
    [Serializable]
    public sealed class EventCallData
    {
        public string target, method, argumentType;
        public int mode, state;
        public ValueData argument;
    }
}
