using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimUnity.Serialization;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace RimUnity.Editor
{
    //读取 Unity 的持久事件数据，编辑器运行时添加的监听器不会打包。
    internal static class EventWriter
    {
        //字段名对应 2022.3 UnityEventBase，缺失时明确阻止导出。
        internal static ValueData Write(UnityEventBase ev, ValueWriter writer, Dictionary<Object, string> objects, string path)
        {
            var result = new ValueData { kind = "event", calls = new List<EventCallData>() };
            object group = Field(ev, "m_PersistentCalls");
            foreach (object call in (IEnumerable)Field(group, "m_Calls"))
            {
                int state = Convert.ToInt32(Field(call, "m_CallState"));
                if (state == 0) continue;
                Object target = (Object)Field(call, "m_Target");
                if (target == null || !objects.TryGetValue(target, out string id)) throw new NotSupportedException(path + "：事件目标必须位于导出的预制体内。");
                int mode = Convert.ToInt32(Field(call, "m_Mode"));
                object arguments = Field(call, "m_Arguments");
                string argumentType = ((string)Field(arguments, "m_ObjectArgumentAssemblyTypeName")).Split(',')[0];
                object argument = null;
                switch (mode)
                {
                    case 2: argument = Field(arguments, "m_ObjectArgument"); break;
                    case 3: argument = Field(arguments, "m_IntArgument"); break;
                    case 4: argument = Field(arguments, "m_FloatArgument"); break;
                    case 5: argument = Field(arguments, "m_StringArgument"); break;
                    case 6: argument = Field(arguments, "m_BoolArgument"); break;
                }
                result.calls.Add(new EventCallData { target = id, method = (string)Field(call, "m_MethodName"), mode = mode, state = state,
                    argumentType = ComponentWriter.MapTypeName(argumentType), argument = writer.Write(argument, path + ".argument") });
            }
            return result;
        }

        //显式检查编辑器版本变化，避免生成缺失事件的包。
        private static object Field(object owner, string name)
        {
            for (Type t = owner.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(owner);
            }
            throw new MissingFieldException(owner.GetType().FullName, name);
        }
    }
}
