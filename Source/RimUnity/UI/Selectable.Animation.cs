namespace RimUnity.UI;

//状态发生变化时触发动画，避免每次绘制重新发送 Trigger。
public abstract partial class Selectable
{
    public AnimationTriggers animationTriggers { get; set; } = new();
    public Animator animator => GetComponent<Animator>();
    private string activeAnimationTrigger;
    private Animator activeAnimator;
    private RuntimeAnimatorController activeController;

    //沿用 UGUI 的禁用、按下、选中、悬停、普通状态优先级。
    protected virtual void Update()
    {
        if (transition != Transition.Animation) { activeAnimationTrigger = null; return; }
        Animator target = animator;
        if (target == null || !target.isActiveAndEnabled || target.runtimeAnimatorController == null) return;
        string trigger = !IsInteractable() ? animationTriggers.disabledTrigger : Pressed ? animationTriggers.pressedTrigger :
            selected ? animationTriggers.selectedTrigger : Hovered ? animationTriggers.highlightedTrigger : animationTriggers.normalTrigger;
        if (trigger == activeAnimationTrigger && target == activeAnimator && target.runtimeAnimatorController == activeController) return;
        activeAnimator = target; activeController = target.runtimeAnimatorController; activeAnimationTrigger = trigger;
        if (string.IsNullOrEmpty(trigger)) return;
        foreach (var parameter in target.parameters)
            if (parameter.type == AnimatorControllerParameterType.Trigger &&
                (parameter.name == animationTriggers.normalTrigger || parameter.name == animationTriggers.highlightedTrigger ||
                 parameter.name == animationTriggers.pressedTrigger || parameter.name == animationTriggers.selectedTrigger || parameter.name == animationTriggers.disabledTrigger))
                target.ResetTrigger(parameter.name);
        target.SetTrigger(trigger);
    }
}
