using RimUnity.EventSystems;
using UnityEngine;

namespace RimUnity.UI.Runtime;

//把当前 GUI 键盘事件发送到桥接界面的焦点对象。
internal static class UguiKeyboard
{
    //先让焦点对象消费原始输入，再执行默认导航和提交。
    internal static void Process()
    {
        EventSystem system = EventSystem.current;
        if (system == null) return;
        if (system.currentSelectedGameObject != null && !system.currentSelectedGameObject.activeInHierarchy)
            system.SetSelectedGameObject(null);
        Event ev = Event.current;
        if (ev.type != EventType.KeyDown && ev.type != EventType.KeyUp && ev.type != EventType.Repaint) return;
        BaseEventData update = new(system);
        EventSystem.Send<IUpdateSelectedHandler>(system.currentSelectedGameObject, h => h.OnUpdateSelected(update));
        if (update.used) { if (ev.isKey) ev.Use(); return; }
        if (!system.sendNavigationEvents || ev.type != EventType.KeyDown) return;
        MoveDirection direction = ev.keyCode switch
        {
            KeyCode.LeftArrow => MoveDirection.Left, KeyCode.RightArrow => MoveDirection.Right,
            KeyCode.UpArrow => MoveDirection.Up, KeyCode.DownArrow => MoveDirection.Down,
            _ => MoveDirection.None
        };
        bool submit = ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space;
        bool cancel = ev.keyCode == KeyCode.Escape;
        if (direction == MoveDirection.None && !submit && !cancel) return;
        if (system.currentSelectedGameObject == null && !cancel)
        {
            GameObject first = system.firstSelectedGameObject;
            if (first == null || !first.activeInHierarchy)
            {
                foreach (Selectable item in Selectable.allSelectablesArray)
                    if (item.IsInteractable() && item.navigation.mode != Navigation.Mode.None) { first = item.gameObject; break; }
            }
            if (first != null && first.activeInHierarchy) { system.SetSelectedGameObject(first); ev.Use(); }
            return;
        }
        if (direction != MoveDirection.None)
        {
            AxisEventData data = new(system) { moveDir = direction, moveVector = direction switch
            {
                MoveDirection.Left => Vector2.left, MoveDirection.Right => Vector2.right,
                MoveDirection.Up => Vector2.up, _ => Vector2.down
            } };
            if (EventSystem.Send<IMoveHandler>(system.currentSelectedGameObject, h => h.OnMove(data))) ev.Use();
        }
        else
        {
            BaseEventData data = new(system);
            bool handled = submit ? EventSystem.Send<ISubmitHandler>(system.currentSelectedGameObject, h => h.OnSubmit(data)) :
                EventSystem.Send<ICancelHandler>(system.currentSelectedGameObject, h => h.OnCancel(data));
            if (handled || data.used) ev.Use();
        }
    }
}
