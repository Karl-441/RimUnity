using System;
using HarmonyLib;
using UnityEngine;
using Waylith.UnityCompat;

namespace RimUnity.Assets;

//本机引擎缺少克隆工厂时会返回原对象，桥接对象不能静默产生这种别名。
[HarmonyPatch(typeof(UnityWorld), nameof(UnityWorld.InstantiateObject))]
internal static class CloneGuard
{
    //仅检查本模组创建的对象，不改动游戏或其它模组的克隆行为。
    private static void Prefix(object __0)
    {
        GameObject owner = __0 as GameObject ?? (__0 as Component)?.gameObject;
        if (!OwnedObjects.Contains(owner)) return;
        if (__0 is GameObject gameObject && OwnedObjects.IsTemplate(gameObject)) return;
        throw new NotSupportedException("当前请使用 PrefabLoader.Load 返回的根模板创建实例；场景对象及局部子树克隆尚未接通。");
    }
}
