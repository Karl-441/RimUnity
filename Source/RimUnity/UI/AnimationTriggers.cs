namespace RimUnity.UI;

//保存 Selectable 各状态对应的 Animator Trigger 名称。
public sealed class AnimationTriggers
{
    public string normalTrigger { get; set; } = "Normal";
    public string highlightedTrigger { get; set; } = "Highlighted";
    public string pressedTrigger { get; set; } = "Pressed";
    public string selectedTrigger { get; set; } = "Selected";
    public string disabledTrigger { get; set; } = "Disabled";
}
