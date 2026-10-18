using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AIExpAnalyzePanel : MonoBehaviour
{
    [SerializeField] Button sendBtn;
    [SerializeField] TextMeshProUGUI answerText;
    [Header("实验分析（可选，Inspector 调试用）")]
    [TextArea(5, 15)]
    public string experimentDataJson;
    void Start()
    {
        sendBtn.onClick.AddListener(SendAIAnalyze);
    }

    public void SendAIAnalyze()
    {
        sendBtn.interactable = false;
        MarkdownRenderer.Instance.Init(answerText);
        AIClient.Instance.SendAnalyze(
            experimentDataJson,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            ()=>CompetedAIAswer()
        );
    }

    void CompetedAIAswer()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(answerText.transform.parent as RectTransform);
        sendBtn.interactable = true;
    }
}
