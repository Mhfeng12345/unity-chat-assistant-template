using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class AIPanel : MonoBehaviour
{
    [SerializeField] TMP_Dropdown selectAIModeDp;
    [SerializeField] GameObject AIKnowledgeDBChatChatPanel, AIExpAnalyzePanel;
    void Start()
    {
        selectAIModeDp.onValueChanged.AddListener(value =>
        {
            AIKnowledgeDBChatChatPanel.SetActive(value == 0);
            AIExpAnalyzePanel.SetActive(value == 1);
        });
        selectAIModeDp.onValueChanged.Invoke(selectAIModeDp.value);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
