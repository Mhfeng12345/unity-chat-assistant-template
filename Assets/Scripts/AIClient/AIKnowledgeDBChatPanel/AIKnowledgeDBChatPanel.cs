using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AIKnowledgeDBChatPanel : MonoBehaviour
{
    [SerializeField] TMP_InputField userInput;
    [SerializeField] Button sendBtn;
    [SerializeField] RectTransform content;
    [SerializeField] GameObject questionItemPrefab, answerItemPrefab;
    private void Start()
    {
        sendBtn.onClick.AddListener(() =>OnInputSubmit(userInput.text));
        userInput.onSubmit.AddListener(OnInputSubmit);
    }

    private void OnInputSubmit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        //用户问题
        var questionItem = Instantiate(questionItemPrefab, content);
        questionItem.GetComponentInChildren<TMP_Text>().text = text;

        //回答项 + 渲染器
        var answerItem = Instantiate(answerItemPrefab, content);
        var aText = answerItem.GetComponentInChildren<TMP_Text>();
        var renderer=MarkdownRenderer.Instance;
        renderer.Init(aText);

        //流式
        AIClient.Instance.SendExperimentQA(
            text,
            chunk => renderer.Append(chunk),
            () =>
            {
                renderer.RenderNow();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }
        );

        //收尾
        userInput.text = "";
        userInput.ActivateInputField();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

    }
}
