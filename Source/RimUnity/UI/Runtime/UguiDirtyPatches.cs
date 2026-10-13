using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Waylith.UnityCompat;

namespace RimUnity.UI.Runtime;

//仅对桥接拥有的矩形对象记录属性与结构变化，不修改宿主行为。
internal static class UguiDirtyPatches
{
    //构造和资源模板阶段尚无场景布局，不查询未登记对象的 transform。
    private static Transform OwnedTransform(Component component)
    {
        if (UnityRuntime.Current is not { } runtime || runtime.World.GetInstanceId(component) == 0) return null;
        GameObject owner = (GameObject)runtime.World.GetGameObject(component);
        return OwnedObjects.Contains(owner) && !OwnedObjects.IsTemplate(owner) && owner.transform is RectTransform ? owner.transform : null;
    }

    [HarmonyPatch]
    private static class LayoutProperties
    {
        private static readonly Dictionary<MethodBase, Func<Component, object>> Getters = new();
        private readonly record struct Before(Transform Transform, Func<Component, object> Read, object Value);

        //仅选择实际影响布局的属性，同值写入不会标脏。
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var types = new (Type Type, string[] Names)[]
            {
                (typeof(RectTransform), new[] { "anchorMin", "anchorMax", "pivot", "sizeDelta", "offsetMin", "offsetMax" }),
                (typeof(UnityEngine.Transform), new[] { "localScale" }),
                (typeof(UnityEngine.Behaviour), new[] { "enabled" }),
                (typeof(UnityEngine.UI.Text), new[] { "text", "font", "fontSize", "fontStyle", "alignment", "supportRichText", "lineSpacing" }),
                (typeof(UnityEngine.UI.Image), new[] { "sprite", "overrideSprite", "type", "pixelsPerUnitMultiplier" }),
                (typeof(Text), null),
                (typeof(TextMeshProUGUI), new[] { "text", "font", "fontSize", "fontSizeMin", "fontSizeMax", "enableAutoSizing", "enableWordWrapping", "richText", "enableKerning", "alignment", "characterSpacing", "lineSpacing", "margin" }),
                (typeof(LayoutElement), null),
                (typeof(UguiLayoutGroup), null),
                (typeof(HorizontalOrVerticalLayoutGroup), null),
                (typeof(UnityEngine.UI.LayoutGroup), new[] { "padding", "childAlignment" }),
                (typeof(UnityEngine.UI.GridLayoutGroup), null),
                (typeof(UnityEngine.UI.ContentSizeFitter), null),
                (typeof(AspectRatioFitter), null),
                (typeof(CanvasScaler), null),
                (typeof(Canvas), new[] { "renderMode", "worldCamera", "scaleFactor", "referencePixelsPerUnit" }),
                (typeof(ScrollRect), new[] { "horizontalScrollbar", "verticalScrollbar", "horizontalScrollbarVisibility",
                    "verticalScrollbarVisibility", "horizontalScrollbarSpacing", "verticalScrollbarSpacing" })
            };
            foreach (var item in types)
                foreach (PropertyInfo property in item.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (item.Names != null && Array.IndexOf(item.Names, property.Name) < 0) continue;
                    MethodInfo setter = property.SetMethod;
                    if (setter == null || !setter.IsPublic || property.GetMethod == null || property.GetIndexParameters().Length != 0) continue;
                    if (Getters.ContainsKey(setter)) continue;
                    ParameterExpression component = Expression.Parameter(typeof(Component));
                    Expression read = Expression.Property(Expression.Convert(component, item.Type), property);
                    Getters.Add(setter, Expression.Lambda<Func<Component, object>>(Expression.Convert(read, typeof(object)), component).Compile());
                    yield return setter;
                }
        }

        //读取写入前的真实属性值，布局自身写回时跳过。
        private static void Prefix(Component __instance, MethodBase __originalMethod, out Before __state)
        {
            __state = default;
            if (UguiDirty.RebuildDepth > 0) return;
            Transform transform = OwnedTransform(__instance);
            if (transform == null) return;
            Func<Component, object> getter = Getters[__originalMethod];
            __state = new Before(transform, getter, getter(__instance));
        }

        //按写入后的值比较，避免画布每次准备时的同值赋值污染缓存。
        private static void Postfix(Component __instance, Before __state)
        {
            if (__state.Transform != null && !Equals(__state.Value, __state.Read(__instance))) UguiDirty.Mark(__state.Transform);
        }
    }

    [HarmonyPatch(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) })]
    private static class Reparent
    {
        //旧父布局需要在节点脱离前标记。
        private static void Prefix(Transform __instance) { Transform owned = OwnedTransform(__instance); if (owned != null) UguiDirty.Mark(owned); }
        //新父布局在节点挂接后标记。
        private static void Postfix(Transform __instance) { Transform owned = OwnedTransform(__instance); if (owned != null) UguiDirty.Mark(owned); }
    }

    [HarmonyPatch(typeof(Transform), nameof(Transform.SetSiblingIndex))]
    private static class Reorder
    {
        //排列顺序会改变网格和线性布局。
        private static void Postfix(Transform __instance) { Transform owned = OwnedTransform(__instance); if (owned != null) UguiDirty.Mark(owned); }
    }

    [HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
    private static class Active
    {
        //记录是否真正发生激活状态变化。
        private static void Prefix(GameObject __instance, out bool __state) => __state = __instance.activeSelf;
        //停用节点也要让仍活动的父布局重算。
        private static void Postfix(GameObject __instance, bool __state)
        {
            if (OwnedObjects.Contains(__instance) && __instance.activeSelf != __state) UguiDirty.Mark(__instance.transform);
        }
    }

    [HarmonyPatch(typeof(GameObject), "Attach")]
    private static class Components
    {
        //泛型和非泛型 AddComponent 都经过此入口。
        private static void Postfix(GameObject __instance)
        {
            if (OwnedObjects.Contains(__instance)) UguiDirty.Mark(__instance.transform);
        }
    }

    [HarmonyPatch]
    private static class Destroy
    {
        //两个真实销毁入口分别覆盖即时与延迟销毁。
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UnityEngine.Object), nameof(UnityEngine.Object.Destroy), new[] { typeof(UnityEngine.Object) });
            yield return AccessTools.Method(typeof(UnityEngine.Object), nameof(UnityEngine.Object.DestroyImmediate), new[] { typeof(UnityEngine.Object) });
        }
        //在对象失去父级之前记住受影响的缓存。
        private static void Prefix(UnityEngine.Object obj)
        {
            Transform transform = obj is Component component ? OwnedTransform(component) :
                obj is GameObject go && OwnedObjects.Contains(go) ? go.transform : null;
            if (transform != null) UguiDirty.TrackDestroy(obj, transform);
        }
    }
}
