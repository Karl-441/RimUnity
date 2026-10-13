//源自 ChezhouLib UGUI；迁移来源与第三方许可见 Docs/ThirdParty.md。
using UnityEngine;
namespace RimUnity.EventSystems;

//保存接入模组的焦点状态；指针输入由 CL 画布统一处理。
public class EventSystem : UnityEngine.EventSystems.EventSystem
{
    private static EventSystem instance;
    public new static EventSystem current => instance;
    public bool sendNavigationEvents { get; set; } = true;
    public int pixelDragThreshold { get; set; } = 10;
    public GameObject currentSelectedGameObject { get; private set; }
    public GameObject firstSelectedGameObject { get; set; }
    public bool alreadySelecting { get; private set; }
    //组件进入场景时注册事件系统。
    private void OnEnable()
    {
        instance = this;
        if (firstSelectedGameObject != null && firstSelectedGameObject.activeInHierarchy) SetSelectedGameObject(firstSelectedGameObject);
    }
    //禁用的事件系统不继续持有输入焦点。
    private void OnDisable() { SetSelectedGameObject(null); if (instance == this) instance = null; }
    //销毁时清除对应实例。
    private void OnDestroy() { if (instance == this) instance = null; }
    //焦点切换按顺序通知旧控件与新控件，回调中不重复进入选择过程。
    public void SetSelectedGameObject(GameObject selected) => SetSelectedGameObject(selected, new BaseEventData(this));
    //使用输入方提供的事件对象派发焦点变化。
    public void SetSelectedGameObject(GameObject selected, BaseEventData data)
    {
        if (alreadySelecting || selected == currentSelectedGameObject) return;
        alreadySelecting = true;
        try
        {
            Send<IDeselectHandler>(currentSelectedGameObject, h => h.OnDeselect(data));
            currentSelectedGameObject = selected;
            Send<ISelectHandler>(selected, h => h.OnSelect(data));
        }
        finally { alreadySelecting = false; }
    }
    //查询桥接画布的实际命中结果。
    public new bool IsPointerOverGameObject() => UI.Runtime.UguiPointer.IsOverUi;
    //同一焦点对象上的活动组件依次接收事件。
    internal static bool Send<T>(GameObject target, System.Action<T> call)
    {
        if (target == null) return false;
        bool handled = false;
        foreach (T handler in target.GetComponents<T>())
            if (handler is not Behaviour b || b.isActiveAndEnabled) { call(handler); handled = true; }
        return handled;
    }
}
