using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AIClient : MonoBehaviour
{
    public static AIClient Instance;

    [Header("配置")]
    [Tooltip("StreamingAssets 下的配置文件名")]
    [SerializeField] private string configFileName = "AIConfig.json";

    private string analyzeUrl;
    private string chatUrl;
    private string experimentQAUrl;

    private readonly ConcurrentQueue<byte[]> chunks = new();
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder buffer = new();
    private readonly StringBuilder rawBody = new();
    private readonly char[] chars = new char[8192];

    private Action<string> onChunk;
    private Action onComplete;

    [Serializable]
    private class AIConfig
    {
        public string aiUrl;
    }

    [Serializable]
    private class StreamResponse
    {
        public StreamChoice[] choices;
    }

    [Serializable]
    private class StreamChoice
    {
        public Delta delta;
    }

    [Serializable]
    private class Delta
    {
        public string content;
    }

    [Serializable]
    private class ChatRequestBody
    {
        public string message;
    }

    [Serializable]
    private class ExperimentQARequestBody
    {
        public string experiment_name;
        public string question;
    }

    private void Awake()
    {
        Instance = this;
        StartCoroutine(LoadConfig());
    }

    private IEnumerator LoadConfig()
    {
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);

#if UNITY_ANDROID && !UNITY_EDITOR
        using var request = UnityWebRequest.Get(path);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"加载配置失败: {request.error}\n路径: {path}");
            yield break;
        }

        ParseConfig(request.downloadHandler.text);
#else
        if (!File.Exists(path))
        {
            Debug.LogError($"找不到配置文件: {path}");
            yield break;
        }

        string json = File.ReadAllText(path);
        ParseConfig(json);
        yield break;
#endif
    }

    private void ParseConfig(string json)
    {
        try
        {
            AIConfig config = JsonUtility.FromJson<AIConfig>(json);

            if (string.IsNullOrEmpty(config.aiUrl))
            {
                Debug.LogError("AIConfig.aiUrl 为空");
                return;
            }

            // 去掉末尾多余的 /
            string baseUrl = config.aiUrl.TrimEnd('/');

            analyzeUrl = $"{baseUrl}/analyze";
            chatUrl = $"{baseUrl}/chat";
            experimentQAUrl = $"{baseUrl}/knowledgeDBChat";

            Debug.Log($"AIConfig 加载成功\naiUrl: {baseUrl}\nanalyze: {analyzeUrl}\nchat: {chatUrl}\nexperimentQA: {experimentQAUrl}");
        }
        catch (Exception e)
        {
            Debug.LogError($"解析 AIConfig 失败: {e.Message}");
        }
    }

    #region 公共接口

    /// <summary>
    /// 实验分析：直接传入 JSON 字符串
    /// </summary>
    public void SendAnalyze(string json, Action<string> chunkCallback, Action completeCallback = null)
    {
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogError("experimentDataJson 为空");
            completeCallback?.Invoke();
            return;
        }

        if (string.IsNullOrEmpty(analyzeUrl))
        {
            Debug.LogError("analyzeUrl 未配置，请检查 StreamingAssets/AIConfig.json");
            completeCallback?.Invoke();
            return;
        }

        StartCoroutine(SendRequest(analyzeUrl, json, chunkCallback, completeCallback));
    }

    /// <summary>
    /// 普通聊天
    /// </summary>
    public void SendChat(string message, Action<string> chunkCallback, Action completeCallback = null)
    {
        if (string.IsNullOrEmpty(chatUrl))
        {
            Debug.LogError("chatUrl 未配置，请检查 StreamingAssets/AIConfig.json");
            completeCallback?.Invoke();
            return;
        }

        var body = new ChatRequestBody { message = message };
        string json = JsonUtility.ToJson(body);
        StartCoroutine(SendRequest(chatUrl, json, chunkCallback, completeCallback));
    }

    /// <summary>
    /// 实验知识问答：指定实验名称，检索知识库后流式回答
    /// </summary>
    public void SendExperimentQA(string experimentName, string question, Action<string> chunkCallback, Action completeCallback = null)
    {
        if (string.IsNullOrWhiteSpace(experimentName) || string.IsNullOrWhiteSpace(question))
        {
            Debug.LogError("experimentName 或 question 为空");
            completeCallback?.Invoke();
            return;
        }

        if (string.IsNullOrEmpty(experimentQAUrl))
        {
            Debug.LogError("experimentQAUrl 未配置，请检查 StreamingAssets/AIConfig.json");
            completeCallback?.Invoke();
            return;
        }

        var body = new ExperimentQARequestBody
        {
            experiment_name = experimentName,
            question = question
        };

        StartCoroutine(SendRequest(experimentQAUrl, JsonUtility.ToJson(body), chunkCallback, completeCallback));
    }

    #endregion

    #region 流式请求核心

    private IEnumerator SendRequest(string url, string jsonBody, Action<string> chunkCallback, Action completeCallback)
    {
        chunks.Clear();
        buffer.Clear();
        rawBody.Clear();
        decoder.Reset();
        onChunk = chunkCallback;
        onComplete = completeCallback;

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new StreamHandler(chunks);
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 300;

        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            ProcessChunks();
            yield return null;
        }

        ProcessChunks();

        if (buffer.Length > 0)
        {
            ProcessLine(buffer.ToString().TrimEnd('\r'));
            buffer.Clear();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"请求失败：{request.responseCode} {request.error}\n{rawBody}");
        }

        onComplete?.Invoke();
    }

    private void ProcessChunks()
    {
        while (chunks.TryDequeue(out byte[] data))
        {
            int count = decoder.GetChars(data, 0, data.Length, chars, 0, false);
            string text = new string(chars, 0, count);
            buffer.Append(text);
            rawBody.Append(text);
        }

        while (true)
        {
            string text = buffer.ToString();
            int newline = text.IndexOf('\n');
            if (newline < 0) break;

            string line = text.Substring(0, newline).TrimEnd('\r');
            buffer.Remove(0, newline + 1);
            ProcessLine(line);
        }
    }

    private void ProcessLine(string line)
    {
        if (!line.StartsWith("data:")) return;

        string json = line.Substring(5).Trim();
        if (json == "[DONE]") return;

        try
        {
            StreamResponse response = JsonUtility.FromJson<StreamResponse>(json);
            string content = response?.choices?[0]?.delta?.content;
            if (!string.IsNullOrEmpty(content))
            {
                onChunk?.Invoke(content);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"解析流式数据失败：{e.Message}");
        }
    }

    private class StreamHandler : DownloadHandlerScript
    {
        private readonly ConcurrentQueue<byte[]> chunks;

        public StreamHandler(ConcurrentQueue<byte[]> chunks) : base(new byte[8192])
        {
            this.chunks = chunks;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength == 0) return false;

            byte[] copy = new byte[dataLength];
            Buffer.BlockCopy(data, 0, copy, 0, dataLength);
            chunks.Enqueue(copy);
            return true;
        }
    }

    #endregion
}