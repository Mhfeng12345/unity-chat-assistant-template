using TMPro;
using UnityEngine;

namespace PLC_AIPanelUI_Handoff_20261127
{
// Presentation-only mode switch. No AI, experiment data, sending or clearing logic.
public sealed class AIPanel : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown selectAIModeDp;
    [SerializeField] private GameObject AIKnowledgeDBChatChatPanel, AIExpAnalyzePanel;
    private void Awake() { selectAIModeDp.onValueChanged.AddListener(ShowMode); }
    private void OnEnable() { ShowMode(selectAIModeDp.value); }
    private void OnDestroy() { if (selectAIModeDp) selectAIModeDp.onValueChanged.RemoveListener(ShowMode); }
    private void ShowMode(int value)
    {
        AIKnowledgeDBChatChatPanel.SetActive(value == 0);
        AIExpAnalyzePanel.SetActive(value == 1);
    }
}

}
