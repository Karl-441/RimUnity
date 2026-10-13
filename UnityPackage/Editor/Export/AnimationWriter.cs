using System;
using System.Collections.Generic;
using System.Linq;
using RimUnity.Serialization;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RimUnity.Editor
{
    //直接读取已解析的编辑器动画资源，不依赖 Controller 的 YAML 内部布局。
    internal static class AnimationWriter
    {
        private static ValueData Data(params MemberData[] fields) => new ValueData { kind = "data", fields = fields.ToList() };
        private static ValueData ArrayData(IEnumerable<ValueData> items) => new ValueData { kind = "array", items = items.ToList() };
        private static MemberData Field(string name, ValueData value) => new MemberData { name = name, value = value };
        internal static void Clip(AnimationClip clip, ResourceData data, ValueWriter writer, string path)
        {
            if (clip.humanMotion) throw new NotSupportedException(path + "：Humanoid 动画不在首阶段范围。");
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0) throw new NotSupportedException(path + "：对象切换动画曲线尚未接通。");
            data.type = "AnimationClip";
            void Put(string name, object value) => data.members.Add(Field(name, writer.Write(value, path + "." + name)));
            Put("length", clip.length); Put("frameRate", clip.frameRate); Put("isLooping", clip.isLooping);
            data.members.Add(Field("tracks", ArrayData(AnimationUtility.GetCurveBindings(clip).Select(binding =>
            {
                if (binding.type == typeof(Animator)) throw new NotSupportedException(path + "：Root Motion／肌肉曲线尚未接通：" + binding.propertyName);
                return Data(Field("path", writer.Write(binding.path, path)), Field("type", writer.Write(ComponentWriter.MapTypeName(binding.type.FullName), path)),
                    Field("property", writer.Write(binding.propertyName, path)), Field("curve", writer.Write(AnimationUtility.GetEditorCurve(clip, binding), path)));
            }))));
            data.members.Add(Field("events", ArrayData(AnimationUtility.GetAnimationEvents(clip).Select(ev => MathValueWriter.Properties(ev, writer, path,
                "time", "functionName", "floatParameter", "intParameter", "stringParameter", "objectReferenceParameter")))));
        }
        internal static void Controller(AnimatorController controller, ResourceData data, ValueWriter writer, string path)
        {
            data.type = "RuntimeAnimatorController";
            data.members.Add(Field("parameters", ArrayData(controller.parameters.Select(p => MathValueWriter.Properties(p, writer, path, "name", "type", "defaultFloat", "defaultInt", "defaultBool")))));
            data.members.Add(Field("layers", ArrayData(controller.layers.Select(layer => Layer(layer, writer, path)))));
        }
        private static ValueData Layer(AnimatorControllerLayer layer, ValueWriter writer, string path)
        {
            var sm = layer.stateMachine;
            if (layer.syncedLayerIndex >= 0 || layer.iKPass || sm.stateMachines.Length != 0 || sm.entryTransitions.Length != 0 || sm.behaviours.Length != 0)
                throw new NotSupportedException(path + "/" + layer.name + "：同步层／IK／嵌套状态机／Entry Transition／StateMachineBehaviour 尚未接通。");
            if (sm.defaultState == null) throw new InvalidOperationException(path + "/" + layer.name + "：没有默认状态。");
            var result = Data(Field("name", writer.Write(layer.name, path)), Field("defaultState", writer.Write(sm.defaultState.name, path)),
                Field("defaultWeight", writer.Write(layer.defaultWeight, path)), Field("additive", writer.Write(layer.blendingMode == AnimatorLayerBlendingMode.Additive, path)),
                Field("states", ArrayData(sm.states.Select(s => State(s.state, writer, path)))),
                Field("anyTransitions", ArrayData(sm.anyStateTransitions.Select(t => Transition(t, writer, path)))));
            if (layer.avatarMask != null)
            {
                var mask = layer.avatarMask; var paths = new List<ValueData>();
                for (int i = 0; i < mask.transformCount; i++) paths.Add(Data(Field("path", writer.Write(mask.GetTransformPath(i), path)), Field("active", writer.Write(mask.GetTransformActive(i), path))));
                result.fields.Add(Field("mask", ArrayData(paths)));
            }
            return result;
        }
        private static ValueData State(AnimatorState state, ValueWriter writer, string path)
        {
            if (state.behaviours.Length != 0 || state.mirror || state.mirrorParameterActive || state.timeParameterActive || state.cycleOffsetParameterActive)
                throw new NotSupportedException(path + "/" + state.name + "：状态行为／镜像／时间参数尚未接通。");
            return Data(Field("name", writer.Write(state.name, path)), Field("tag", writer.Write(state.tag, path)), Field("speed", writer.Write(state.speed, path)),
                Field("speedParameter", writer.Write(state.speedParameterActive ? state.speedParameter : null, path)), Field("cycleOffset", writer.Write(state.cycleOffset, path)),
                Field("motion", Motion(state.motion, writer, path)), Field("transitions", ArrayData(state.transitions.Where(t => !t.mute).Select(t => Transition(t, writer, path)))));
        }
        private static ValueData Motion(Motion motion, ValueWriter writer, string path)
        {
            if (motion == null) return writer.Write(null, path);
            if (motion is AnimationClip clip) return Data(Field("clip", writer.Write(clip, path)));
            if (!(motion is BlendTree tree) || tree.blendType != BlendTreeType.Simple1D) throw new NotSupportedException(path + "：仅支持一维 BlendTree。");
            var children = tree.children.OrderBy(c => c.threshold).ToArray();
            if (children.Length == 0 || children.Select(c => c.threshold).Distinct().Count() != children.Length) throw new InvalidOperationException(path + "：BlendTree 不能为空或含重复 threshold。");
            if (children.Any(c => c.mirror)) throw new NotSupportedException(path + "：混合树镜像尚未接通。");
            return Data(Field("parameter", writer.Write(tree.blendParameter, path)), Field("children", ArrayData(children.Select(c => Data(
                Field("threshold", writer.Write(c.threshold, path)), Field("timeScale", writer.Write(c.timeScale, path)), Field("cycleOffset", writer.Write(c.cycleOffset, path)), Field("motion", Motion(c.motion, writer, path)))))));
        }
        private static ValueData Transition(AnimatorStateTransition transition, ValueWriter writer, string path)
        {
            if (transition.isExit || transition.destinationState == null || transition.interruptionSource != TransitionInterruptionSource.None)
                throw new NotSupportedException(path + "：Exit／子状态机／过渡中断尚未接通。");
            return Data(Field("destination", writer.Write(transition.destinationState.name, path)), Field("hasExitTime", writer.Write(transition.hasExitTime, path)),
                Field("exitTime", writer.Write(transition.exitTime, path)), Field("fixedDuration", writer.Write(transition.hasFixedDuration, path)), Field("duration", writer.Write(transition.duration, path)),
                Field("offset", writer.Write(transition.offset, path)), Field("canTransitionToSelf", writer.Write(transition.canTransitionToSelf, path)),
                Field("conditions", ArrayData(transition.conditions.Select(c => MathValueWriter.Properties(c, writer, path, "parameter", "mode", "threshold")))));
        }
    }
}
