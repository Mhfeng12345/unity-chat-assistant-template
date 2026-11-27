using UnityEngine;

namespace PLC_AIPanelUI_Handoff_20261127
{
    // Empty-state helper for the handoff UI. Keeps the welcome bubble in sync
    // with the chat list without modifying the business panel script.
    public sealed class ChatWelcomeAutoHide : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private GameObject welcome;

        private bool? lastShown;

        private void LateUpdate()
        {
            if (content == null || welcome == null) return;

            bool show = content.childCount == 0;
            if (lastShown.HasValue && lastShown.Value == show) return;

            lastShown = show;
            welcome.SetActive(show);
        }
    }
}
