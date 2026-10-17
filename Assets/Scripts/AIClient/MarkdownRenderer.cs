using System;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using System.Collections.Generic;

public class MarkdownRenderer : MonoBehaviour
{
    [SerializeField] private Color codeColor = new(0.65f, 0.85f, 0.65f);
    [SerializeField] private Color quoteColor = new(0.65f, 0.65f, 0.65f);
    [SerializeField] private Color inlineCodeColor = new(0.9f, 0.7f, 0.5f);

    [Header("渲染节流")]
    [SerializeField] private float renderInterval = 0.06f;

    private TMP_Text target;

    private readonly StringBuilder buffer = new();
    private readonly StringBuilder result = new();
    private readonly Dictionary<string, string> inlineCodeMap = new();
    private int inlineCodeCounter = 0;
    private bool dirty;
    private float lastRenderTime;
    public static MarkdownRenderer Instance;

    /// <summary>每次真正重渲染完成后触发，布局刷新由外部（ChatUI）负责</summary>
    public event Action OnRendered;

    private void Awake()
    {
        Instance = this;
    }

    public void Init(TMP_Text target)
    {
        this.target = target;
        buffer.Clear(); result.Clear();
        inlineCodeMap.Clear(); inlineCodeCounter = 0;
        if (target != null) target.text = "";
        dirty = false;
        lastRenderTime = 0;
    }

    public void Append(string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        buffer.Append(content);
        dirty = true;
    }

    public void SetContent(string content)
    {
        buffer.Clear(); buffer.Append(content);
        inlineCodeMap.Clear(); inlineCodeCounter = 0;
        dirty = true;
        RenderNow();
    }

    private void Update()
    {
        if (dirty && Time.unscaledTime - lastRenderTime >= renderInterval)
            Render();
    }

    public void RenderNow()
    {
        if (dirty) Render();
    }

    private void Render()
    {
        if (!dirty || target == null) return;
        lastRenderTime = Time.unscaledTime;
        result.Clear();
        inlineCodeMap.Clear();
        inlineCodeCounter = 0;

        string cleanText = SanitizeForTMP(buffer.ToString());
        string[] lines = cleanText.Split('\n');
        bool inCodeBlock = false;
        string codeBlockColor = $"<color=#{ColorUtility.ToHtmlStringRGB(codeColor)}>";

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("```"))
            {
                if (inCodeBlock) result.Append("</color>\n");
                else result.Append(codeBlockColor);
                inCodeBlock = !inCodeBlock;
                continue;
            }
            if (inCodeBlock) { result.Append(Escape(line)).Append('\n'); continue; }
            if (string.IsNullOrWhiteSpace(line)) { result.Append('\n'); continue; }
            if (TryRenderBlock(line, trimmed)) continue;

            result.Append("　　").Append(RenderInline(line)).Append('\n');
        }
        if (inCodeBlock) result.Append("</color>");

        target.text = result.ToString();
        dirty = false;

        OnRendered?.Invoke();
    }

    /// <summary>
    /// ：TMP 格式兜底清洗器
    /// 强制移除或替换 AI 可能偷偷生成的 TMP 不支持的 Markdown 符号
    /// </summary>
    private string SanitizeForTMP(string text)
    {
        text = Regex.Replace(text, @"^[ \t]*[-*+][ \t]+", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^[ \t]*>[ \t]+", "【注】", RegexOptions.Multiline);
        text = Regex.Replace(text, @"```[a-zA-Z]*\n?", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = text.Replace("\t", "    ");
        return text;
    }

    private bool TryRenderBlock(string line, string trimmed)
    {
        int headingLevel = 0;
        while (headingLevel < trimmed.Length && headingLevel < 6 && trimmed[headingLevel] == '#')
            headingLevel++;

        if (headingLevel > 0 && headingLevel < trimmed.Length && trimmed[headingLevel] == ' ')
        {
            string headingText = RenderInline(trimmed.Substring(headingLevel + 1));
            int size = 150 - (headingLevel - 1) * 10;
            result.Append($"<size={size}%><b>{headingText}</b></size>\n");
            return true;
        }

        if (Regex.IsMatch(trimmed, @"^([-*_])\1{2,}\s*$"))
        {
            result.Append("<color=#FFFFFF>──────────────────────────</color>\n");
            return true;
        }

        Match orderedMatch = Regex.Match(line, @"^(\s*)(\d+)\.\s+(.+)$");
        if (orderedMatch.Success)
        {
            string indent = orderedMatch.Groups[1].Value;
            string number = orderedMatch.Groups[2].Value;
            string content = RenderInline(orderedMatch.Groups[3].Value);
            result.Append(indent).Append($"{number}. ").Append(content).Append('\n');
            return true;
        }

        return false;
    }

    private string RenderInline(string text)
    {
        text = Escape(text);

        text = Regex.Replace(text, @"`([^`\n]+)`", match =>
        {
            string code = match.Groups[1].Value;
            string placeholder = $"__INLINE_CODE_{inlineCodeCounter}__";
            string formattedCode = $"<color=#{ColorUtility.ToHtmlStringRGB(inlineCodeColor)}>{code}</color>";
            inlineCodeMap[placeholder] = formattedCode;
            inlineCodeCounter++;
            return placeholder;
        });

        text = Regex.Replace(text, @"\*\*\*(.+?)\*\*\*", "<b><i>$1</i></b>");
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");
        text = Regex.Replace(text, @"(?<!\*)\*([^*\n]+)\*(?!\*)", "<i>$1</i>");

        foreach (var kvp in inlineCodeMap)
            text = text.Replace(kvp.Key, kvp.Value);

        return text;
    }

    private static string Escape(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}