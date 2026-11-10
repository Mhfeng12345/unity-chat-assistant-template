using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 槽式快照 AI 分析消息面板：调用 /analyse/WSTES_SlotType，流式渲染到 Content 下 Text(TMP)。
/// </summary>
public class AIMsgPanel : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI contentText;

    /// <summary>发起槽式快照分析。json 为 V2 中文协议（数据版本/实验模块/采集信息/当前状态/与上次点击相比/辅助计算）。</summary>
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
}

