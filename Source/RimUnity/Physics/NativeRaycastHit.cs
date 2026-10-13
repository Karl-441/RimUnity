using System.Runtime.CompilerServices;
using UnityEngine;

namespace RimUnity;

//宿主命中结构仅公开内部 setter，使用已核对的精确方法签名填写真实查询结果。
internal static class NativeRaycastHit
{
    internal static RaycastHit Create(Collider collider, Vector3 point, Vector3 normal, float distance)
    {
        RaycastHit result = default;
        SetCollider(ref result, collider); SetPoint(ref result, point); SetNormal(ref result, normal); SetDistance(ref result, distance);
        return result;
    }
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_collider")]
    private static extern void SetCollider(ref RaycastHit hit, UnityEngine.Collider value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_point")]
    private static extern void SetPoint(ref RaycastHit hit, Vector3 value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_normal")]
    private static extern void SetNormal(ref RaycastHit hit, Vector3 value);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_distance")]
    private static extern void SetDistance(ref RaycastHit hit, float value);
}
