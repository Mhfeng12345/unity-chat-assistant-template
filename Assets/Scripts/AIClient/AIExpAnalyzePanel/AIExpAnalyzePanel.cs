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

        RefreshSendBtn();
    }

    private void OnSendBtnClick()
    {
        if (isMine && AIClient.Instance.IsBusy)
        {
            AIClient.Instance.StopStream();
            isMine = false;
            RefreshSendBtn();
            return;
        }

        SendAIAnalyze();
    }

    public void SendAIAnalyze()
    {
        isMine = true;
        RefreshSendBtn();

        AIClient.Instance.SendAnalyze(
            experimentDataJson,
            chunk => MarkdownRenderer.Instance.Append(chunk),
            () =>
            {
                isMine = false;
                MarkdownRenderer.Instance.RenderNow();
                LayoutRebuilder.ForceRebuildLayoutImmediate(answerText.transform.parent as RectTransform);
                RefreshSendBtn();
            },
            () => MarkdownRenderer.Instance.Init(answerText));
    }

    private void RefreshSendBtn()
    {
        bool showStop = isMine && AIClient.Instance.IsBusy;
        statusImage.sprite = showStop ? stopSprite : idleSprite;
    }
}
