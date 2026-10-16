using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI answerText;

    [Header("普遍对话")]
    [SerializeField] private string chatStr;
    [Header("实验分析（可选，Inspector 调试用）")]
    [TextArea(5, 15)]
    [SerializeField] private string experimentDataJson;
    [Header("实验知识库问答")]
    [SerializeField] private string currentExperiment;
    [SerializeField] private string experimentQAStr;

    private void FinishLayout()
    {
        Debug.Log("回答完成");

        LayoutRebuilder.ForceRebuildLayoutImmediate(answerText.transform.parent as RectTransform);
    }

    [ContextMenu("Excute Chat")]
    public void SendAIChat()
    {
        MarkdownRenderer.Instance.Init();
        AIClient.Instance.SendChat(
            chatStr,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            FinishLayout
        );
    }

    [ContextMenu("Excute Analyze")]
    public void SendAIAnalyze()
    {
        MarkdownRenderer.Instance.Init();
        AIClient.Instance.SendAnalyze(
            experimentDataJson,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            FinishLayout
        );
    }

    [ContextMenu("Excute ExperimentQA")]
    public void SendAIExperimentQA()
    {
        MarkdownRenderer.Instance.Init();
        AIClient.Instance.SendExperimentQA(
            currentExperiment,
            experimentQAStr,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            FinishLayout
        );
    }
}