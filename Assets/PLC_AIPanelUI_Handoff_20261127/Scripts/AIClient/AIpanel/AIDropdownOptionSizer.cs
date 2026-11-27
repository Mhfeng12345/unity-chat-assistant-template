using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PLC_AIPanelUI_Handoff_20261127
{

[RequireComponent(typeof(TMP_Dropdown))]
public sealed class AIDropdownOptionSizer : MonoBehaviour, IPointerDownHandler, ISubmitHandler
{
    [SerializeField] private float verticalPadding = 12;
    private TMP_Dropdown dropdown;
    private int lastCount = -1;
    private bool wasExpanded;

    private void OnEnable() { RefreshSize(); }

    private void LateUpdate()
    {
        if (dropdown != null && (lastCount != dropdown.options.Count || wasExpanded != dropdown.IsExpanded))
            RefreshSize();
    }

    public void OnPointerDown(PointerEventData eventData) { RefreshSize(); }
    public void OnSubmit(BaseEventData eventData) { RefreshSize(); }

    public void RefreshSize()
    {
        if (dropdown == null) dropdown = GetComponent<TMP_Dropdown>();
        if (dropdown.template == null || dropdown.itemText == null) return;
        var item = dropdown.itemText.transform.parent as RectTransform;
        if (item == null) return;
        float height = item.rect.height * dropdown.options.Count + verticalPadding;
        dropdown.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        var popup = dropdown.transform.Find("Dropdown List") as RectTransform;
        if (popup != null) popup.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        lastCount = dropdown.options.Count;
        wasExpanded = dropdown.IsExpanded;
    }
}

}
