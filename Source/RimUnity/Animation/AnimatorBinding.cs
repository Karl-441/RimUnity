namespace RimUnity;

//保存 Generic Avatar 的原始曲线路径与实际骨骼目标，克隆时引用跟随实例重映射。
public sealed class AnimatorBinding
{
    public string path;
    public Transform target;
}
