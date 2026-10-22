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
    private int requestVersion;

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
            requestVersion++;
            AIClient.Instance.Cancel(this);
            isMine = false;
            clearBtn.interactable = true;
            RefreshSendBtn();
            return;
        }

        OnInputSubmit(userInput.text);
    }

    private void OnInputSubmit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (isMine && AIClient.Instance.IsBusy) return;

        Instantiate(questionItemPrefab, content).GetComponentInChildren<TMP_Text>().text = text;

        var aText = Instantiate(answerItemPrefab, content).GetComponentInChildren<TMP_Text>();
        var renderer = MarkdownRenderer.Instance;
        int version = ++requestVersion;

        string error = AIClient.Instance.SendExperimentQA(
            text,
            chunk =>
            {
                if (version != requestVersion) return;
                renderer.Append(chunk);
            },
            () =>
            {
                if (version != requestVersion) return;
                isMine = false;
                renderer.RenderNow();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                clearBtn.interactable = true;
                RefreshSendBtn();
            },
            () =>
            {
                if (version != requestVersion) return;
                isMine = true;
                clearBtn.interactable = false;
                renderer.Init(aText);
                aText.text = "<color=#999999>AI 思考中…</color>";
                RefreshSendBtn();
            },
            this);

        if (error != null)
        {
            aText.text = $"请求失败：{error}";
            clearBtn.interactable = true;
            RefreshSendBtn();
        }
        else
        {
            userInput.text = "";
            userInput.ActivateInputField();
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    private void ClearAll()
    {
        requestVersion++;
        AIClient.Instance.Cancel(this);
        isMine = false;
        clearBtn.interactable = true;
        RefreshSendBtn();

        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Destroy(content.GetChild(i).gameObject);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    private void RefreshSendBtn()
    {
        bool showStop = isMine && AIClient.Instance.IsBusy;
        statusImage.sprite = showStop ? stopSprite : idleSprite;
        sendBtn.interactable = true;
    }
}
