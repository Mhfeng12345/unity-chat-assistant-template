using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 槽式快照 AI 分析消息面板：调用 /analyse/WSTES_SlotType，流式渲染到 Content 下 Text(TMP)。
/// </summary>
public class AIMsgPanel : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI contentText;

    [TextArea(5,10)][SerializeField] string textJson;
    /// <summary>槽式快照分析</summary>
    public void SendSlotTypeAnalyze(string json)
    {
        string error = AIClient.Instance.SendSlotTypeAnalyze(
            json,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            () =>
            {
                MarkdownRenderer.Instance.RenderNow();
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentText.transform.parent as RectTransform);
            },
            () =>
            {
                MarkdownRenderer.Instance.Init(contentText);
                contentText.text = "<color=#999999>AI 分析中…</color>";
            },
            this);

        if (error != null)
        {
            contentText.text = $"请求失败：{error}";
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentText.transform.parent as RectTransform);
        }
    }


    [ContextMenu("test")]
    public void EditorTest()
    {
        SendSlotTypeAnalyze(textJson);
    }
}

