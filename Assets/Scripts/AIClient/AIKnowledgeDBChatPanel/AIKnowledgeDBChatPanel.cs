using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AIKnowledgeDBChatPanel : MonoBehaviour
{
    [SerializeField] TMP_InputField userInput;
    [SerializeField] Button sendBtn, clearBtn;
    [SerializeField] Image statusImage;
    [SerializeField] Sprite idleSprite, stopSprite;
    [SerializeField] RectTransform content;
    [SerializeField] GameObject questionItemPrefab, answerItemPrefab;

    private bool isMine;

    private void Start()
    {
        sendBtn.onClick.AddListener(OnSendBtnClick);
        userInput.onSubmit.AddListener(OnInputSubmit);
        clearBtn.onClick.AddListener(ClearAll);
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

        OnInputSubmit(userInput.text);
    }

    private void OnInputSubmit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (isMine && AIClient.Instance.IsBusy) return;

        // 用户提问
        var questionItem = Instantiate(questionItemPrefab, content);
        questionItem.GetComponentInChildren<TMP_Text>().text = text;

        // 回答项 + 渲染器
        var answerItem = Instantiate(answerItemPrefab, content);
        var aText = answerItem.GetComponentInChildren<TMP_Text>();
        var renderer = MarkdownRenderer.Instance;

        AIClient.Instance.SendExperimentQA(
            text,
            chunk => renderer.Append(chunk),
            () =>
            {
                isMine = false;
                renderer.RenderNow();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                clearBtn.interactable = true;
                RefreshSendBtn();
            },
            () =>
            {
                isMine = true;
                clearBtn.interactable = false;
                renderer.Init(aText);
                RefreshSendBtn();
            });

        // 收尾
        userInput.text = "";
        userInput.ActivateInputField();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    private void ClearAll()
    {
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Destroy(content.GetChild(i).gameObject);
        }
    }

    private void RefreshSendBtn()
    {
        bool showStop = isMine && AIClient.Instance.IsBusy;
        statusImage.sprite = showStop ? stopSprite : idleSprite;
        sendBtn.interactable = true;
    }
}
