using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI answerText;

    [Header("普通对话")]
    [SerializeField] private string chatStr;
    [Header("实验分析（可选，Inspector 调试用）")]
    [TextArea(5, 15)]
    [SerializeField] private string experimentDataJson;
    [Header("实验知识库问答")]
    [SerializeField] private string currentExperiment;
    [SerializeField] private string experimentQAStr;

    [Header("布局")]
    [SerializeField] private float layoutInterval = 0.08f;
    private float lastLayoutTime;
    private RectTransform contentRect;

    private void Awake()
    {
        contentRect = answerText.transform.parent as RectTransform;
    }

    private void Start()
    {
        MarkdownRenderer.Instance.OnRendered += HandleRendered;
    }

    private void OnDestroy()
    {
        if (MarkdownRenderer.Instance != null)
            MarkdownRenderer.Instance.OnRendered -= HandleRendered;
    }

    private void HandleRendered()
    {
        if (Time.unscaledTime - lastLayoutTime < layoutInterval) return;
        lastLayoutTime = Time.unscaledTime;
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
    }


    private void FinishLayout()
    {
        Debug.Log("回答完成");
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
    }

    [ContextMenu("Excute Chat")]
    public void SendAIChat()
    {
        MarkdownRenderer.Instance.Init(answerText);
        AIClient.Instance.SendChat(
            chatStr,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            FinishLayout
        );
    }

    [ContextMenu("Excute Analyze")]
    public void SendAIAnalyze()
    {
        MarkdownRenderer.Instance.Init(answerText);
        AIClient.Instance.SendAnalyze(
            experimentDataJson,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            FinishLayout
        );
    }

    [ContextMenu("Excute ExperimentQA")]
    public void SendAIExperimentQA()
    {
        MarkdownRenderer.Instance.Init(answerText);
        AIClient.Instance.SendExperimentQA(
            currentExperiment,
            experimentQAStr,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            FinishLayout
        );
    }
}