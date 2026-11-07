using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AIExpAnalyzePanel : MonoBehaviour
{
    [SerializeField] Button sendBtn;
    [SerializeField] Image statusImage;
    [SerializeField] Sprite idleSprite, stopSprite;
    [SerializeField] TextMeshProUGUI answerText;

    [Header("实验数据（可在 Inspector 中配置）")]
    [TextArea(5, 15)]
    public string experimentDataJson;

    private bool isMine;

    private void Start()
    {
        sendBtn.onClick.AddListener(OnSendBtnClick);
        statusImage.color = Color.white;

        // 生产模式（AIConfig.json 未开 isTestMode）：第一帧清空 Inspector 配置的实验数据，
        // 正式数据由业务代码运行时赋值，避免把联调用假数据带回生产
        if (AIClient.Instance != null && !AIClient.Instance.isTestMode)
            experimentDataJson = "";

        RefreshSendBtn();
    }

    private void OnSendBtnClick()
    {
        if (isMine && AIClient.Instance.IsBusy)
        {
            AIClient.Instance.Cancel(this);
            isMine = false;
            RefreshSendBtn();
            return;
        }

        SendAIAnalyze();
    }

    public void SendAIAnalyze()
    {
        string error = AIClient.Instance.SendAnalyze(
            experimentDataJson,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            () =>
            {
                isMine = false;
                MarkdownRenderer.Instance.RenderNow();
                LayoutRebuilder.ForceRebuildLayoutImmediate(answerText.transform.parent as RectTransform);
                RefreshSendBtn();
            },
            () =>
            {
                isMine = true;
                MarkdownRenderer.Instance.Init(answerText);
                answerText.text = "<color=#999999>AI 思考中…</color>";
                RefreshSendBtn();
            },
            this);

        if (error != null)
        {
            answerText.text = $"请求失败：{error}";
            isMine = false;
            LayoutRebuilder.ForceRebuildLayoutImmediate(answerText.transform.parent as RectTransform);
            RefreshSendBtn();
        }
    }

    private void RefreshSendBtn()
    {
        bool showStop = isMine && AIClient.Instance.IsBusy;
        statusImage.sprite = showStop ? stopSprite : idleSprite;
    }
}
