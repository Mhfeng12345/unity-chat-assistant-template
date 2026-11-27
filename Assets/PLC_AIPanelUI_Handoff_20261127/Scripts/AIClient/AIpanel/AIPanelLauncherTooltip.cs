using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PLC_AIPanelUI_Handoff_20261127
{

[RequireComponent(typeof(Button))]
public sealed class AIPanelLauncherTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private GameObject bubble;
    private AILauncherVideoPlayer animationPlayer;

    private void OnEnable() { Hide(); }
    private void OnDisable() { Hide(); }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (bubble != null && isActiveAndEnabled && GetComponent<Button>().IsInteractable())
        {
            bubble.SetActive(true);
            SetSpeaking(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData) { Hide(); }
    public void OnPointerClick(PointerEventData eventData) { Hide(); }

    private void Hide()
    {
        if (bubble != null) bubble.SetActive(false);
        SetSpeaking(false);
    }

    private void SetSpeaking(bool value)
    {
        if (animationPlayer == null) animationPlayer = GetComponent<AILauncherVideoPlayer>();
        if (animationPlayer != null) animationPlayer.SetSpeaking(value);
    }
}

}
