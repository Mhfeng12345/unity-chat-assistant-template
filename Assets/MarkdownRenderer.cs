using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI; 
using System.Collections.Generic;

public class MarkdownRenderer : MonoBehaviour
{
    [SerializeField] private TMP_Text target;
    [SerializeField] private Color codeColor = new(0.65f, 0.85f, 0.65f);
    [SerializeField] private Color quoteColor = new(0.65f, 0.65f, 0.65f);
    [SerializeField] private Color inlineCodeColor = new(0.9f, 0.7f, 0.5f);

    // ===== 布局节流 =====
    [SerializeField] private float layoutInterval = 0.08f;
    private float lastLayoutTime;
    private RectTransform contentRect;


    private readonly StringBuilder buffer = new();
    private readonly StringBuilder result = new();
    private readonly Dictionary<string, string> inlineCodeMap = new();
    private int inlineCodeCounter = 0;
    private bool dirty;
    public static MarkdownRenderer Instance;

    private void Awake()
    {
        Instance = this;
        // 自动找到 Content（Text 的父级）
        contentRect = target.transform.parent as RectTransform;
    }

    public void Init()
    {
        buffer.Clear(); result.Clear();
        inlineCodeMap.Clear(); inlineCodeCounter = 0;
        target.text = ""; dirty = false;
        lastLayoutTime = 0;
    }

    public void Append(string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        buffer.Append(content);
        dirty = true;
        Render();
    }

    public void SetContent(string content)
    {
        buffer.Clear(); buffer.Append(content);
        inlineCodeMap.Clear(); inlineCodeCounter = 0;
        dirty = true;
        Render(forceLayout: true);   // 最终一次强制刷新
    }

    //forceLayout 参数
    private void Render(bool forceLayout = false)
    {
        if (!dirty) return;
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

        // ===== 节流布局刷新 =====
        if (forceLayout || Time.unscaledTime - lastLayoutTime >= layoutInterval)
        {
            lastLayoutTime = Time.unscaledTime;
            if (contentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            }
        }
    }


    /// <summary>
    /// ：TMP 格式兜底清洗器
    /// 强制移除或替换 AI 可能偷偷生成的 TMP 不支持的 Markdown 符号
    /// </summary>
    private string SanitizeForTMP(string text)
    {
        // 1. 强制移除行首的无序列表符号 (-, *, +) 及其后的空格，防止渲染出圆点
        // 兼容行首有空格缩进的情况
        text = Regex.Replace(text, @"^[ \t]*[-*+][ \t]+", "", RegexOptions.Multiline);

        // 2. 将引用符号 > 替换为安全的中文前缀，防止渲染异常
        text = Regex.Replace(text, @"^[ \t]*>[ \t]+", "【注】", RegexOptions.Multiline);

        // 3. 清理 AI 可能生成的代码块标记 ```
        text = Regex.Replace(text, @"```[a-zA-Z]*\n?", "", RegexOptions.Multiline);

        // 4. 清理过多的连续换行（TMP 对连续换行非常敏感，会导致巨大空白）
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        // 5. 将 Tab 缩进替换为普通空格（TMP 对 Tab 支持极差）
        text = text.Replace("\t", "    ");

        return text;
    }

    private bool TryRenderBlock(string line, string trimmed)
    {
        // 标题处理
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

        // 水平线
        if (Regex.IsMatch(trimmed, @"^([-*_])\1{2,}\s*$"))
        {
            result.Append("<color=#FFFFFF>──────────────────────────</color>\n");
            return true;
        }

        // 有序列表 (1. 2. 3.)
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

        // 提取行内代码
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