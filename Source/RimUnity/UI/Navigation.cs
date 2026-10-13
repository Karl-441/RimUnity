namespace RimUnity.UI;

//保存自动方向导航或显式指定的相邻控件。
public struct Navigation
{
    [System.Flags]
    public enum Mode { None = 0, Horizontal = 1, Vertical = 2, Automatic = 3, Explicit = 4 }
    public Mode mode { get; set; }
    public bool wrapAround { get; set; }
    public Selectable selectOnLeft { get; set; }
    public Selectable selectOnRight { get; set; }
    public Selectable selectOnUp { get; set; }
    public Selectable selectOnDown { get; set; }
    public static Navigation defaultNavigation => new() { mode = Mode.Automatic };
}
