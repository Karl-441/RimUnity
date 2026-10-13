using System;

namespace RimUnity;

public enum PhysicMaterialCombine { Average, Minimum, Multiply, Maximum }

//接触材质提供静摩擦、动摩擦和恢复系数，组合优先级与 Unity 枚举一致。
public class PhysicMaterial
{
    public string name;
    public float dynamicFriction = .6f, staticFriction = .6f, bounciness;
    public PhysicMaterialCombine frictionCombine, bounceCombine;
    public PhysicMaterial() { }
    public PhysicMaterial(string name) => this.name = name;
    internal static float Combine(float first, float second, PhysicMaterialCombine a, PhysicMaterialCombine b)
        => (PhysicMaterialCombine)Math.Max((int)a, (int)b) switch
        {
            PhysicMaterialCombine.Average => (first + second) * .5f,
            PhysicMaterialCombine.Minimum => Math.Min(first, second),
            PhysicMaterialCombine.Multiply => first * second,
            PhysicMaterialCombine.Maximum => Math.Max(first, second),
            _ => throw new ArgumentOutOfRangeException(nameof(a))
        };
}
