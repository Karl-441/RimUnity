using System;
using System.Reflection;
using UnityEngine;

namespace RimUnity;

//创建具有矩形变换的引擎对象，供程序化 UI 使用。
public static class Ugui
{
    //同时用于明文加载器，避免创建重复的 Transform。
    internal static GameObject Create(string name, bool rect, bool token)
    {
        var result = (GameObject)Constructor.Invoke(new object[] { name, rect ? new RectTransform() : new Transform(), token });
        OwnedObjects.Register(result);
        return result;
    }

    private static readonly ConstructorInfo Constructor = typeof(GameObject).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(Transform), typeof(bool) }, null)
        ?? throw new MissingMethodException("目标游戏缺少带 Transform 参数的 GameObject 构造函数。");

    //添加组件会遵守引擎的普通动态创建生命周期。
    public static GameObject CreateObject(string name, params Type[] components)
    {
        GameObject result = Create(name, true, false);
        foreach (Type type in components) result.AddComponent(type);
        return result;
    }

    //补齐本机兼容层的父级组件查询。
    public static T GetComponentInParent<T>(this Component component) where T : Component
        => GetComponentInParent<T>(component.gameObject);

    //查找包括自身在内的最近活动父级。
    public static T GetComponentInParent<T>(this GameObject obj) where T : Component
    {
        for (Transform t = obj.transform; t != null; t = t.parent)
        {
            if (!t.gameObject.activeInHierarchy && t != obj.transform) continue;
            T value = t.GetComponent<T>();
            if (value != null) return value;
        }
        return null;
    }
}
