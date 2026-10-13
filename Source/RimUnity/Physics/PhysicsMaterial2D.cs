namespace RimUnity;

//2D 接触材质使用单一摩擦系数和恢复系数。
public sealed class PhysicsMaterial2D : PhysicMaterial
{
    public float friction { get => dynamicFriction; set => staticFriction = dynamicFriction = value; }
    public PhysicsMaterial2D() { friction = .4f; }
    public PhysicsMaterial2D(string name) : this() => this.name = name;
}
