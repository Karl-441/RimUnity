using System;
using System.Collections.Generic;
using System.Linq;
using RimUnity.Serialization;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //Generic Avatar 由 Unity 自己解析目标，导出显式引用，不按骨骼名称猜层级。
    internal static class AnimatorBindingWriter
    {
        internal static void Write(Animator animator, ComponentData data, ValueWriter writer, string path)
        {
            if (animator.avatar == null || animator.runtimeAnimatorController == null) return;
            if (!animator.avatar.isValid || animator.avatar.isHuman)
                throw new NotSupportedException(path + "：需要有效的 Generic Avatar。");
            var targets = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips.Distinct())
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    UnityEngine.Object animated = AnimationUtility.GetAnimatedObject(animator.gameObject, binding);
                    Transform target = animated is GameObject go ? go.transform : (animated as Component)?.transform;
                    if (target == null || !target.IsChildOf(animator.transform))
                        throw new NotSupportedException(path + "：Unity 未将 Generic Avatar 轨道绑定到本对象子树：" + clip.name + "/" + binding.path + "/" + binding.propertyName);
                    if (targets.TryGetValue(binding.path, out Transform previous) && previous != target)
                        throw new NotSupportedException(path + "：不同轨道的 Generic Avatar 路径指向不同节点：" + binding.path);
                    targets[binding.path] = target;
                }
            data.members.Add(new MemberData { name = "avatarBindings", value = new ValueData {
                kind = "array", items = targets.OrderBy(t => t.Key, StringComparer.Ordinal).Select(pair => new ValueData {
                    kind = "data", fields = new List<MemberData> {
                        new MemberData { name = "path", value = writer.Write(pair.Key, path + ".avatarBindings.path") },
                        new MemberData { name = "target", value = writer.Write(pair.Value, path + ".avatarBindings.target") }
                    }
                }).ToList()
            } });
        }
    }
}
