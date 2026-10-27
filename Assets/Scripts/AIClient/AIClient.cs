using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using static AIUsageTracker;

public class AIClient : MonoBehaviour
{
    public static AIClient Instance;

#if UNITY_WEBGL && !UNITY_EDITOR
    // 浏览器环境：走 JS XHR 增量读取（UnityWebRequest 在 WebGL 构建会被浏览器整体缓冲，无法流式）
    // 全限定名：即使 IDE 清理 using 也不会丢（__Internal 绑定只看方法名）
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void AIClientStreamBegin(string gameObjectName, string url, string jsonBody);

    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void AIClientStreamAbort();
#endif

    [Header("配置")]
    [Tooltip("StreamingAssets 下的配置文件名")]
    [SerializeField] private string configFileName = "AIConfig.json";

    public string currentExperimentKey;
    private string analyzeUrl;
    private string chatUrl;
    private string experimentQAUrl;

    /// <summary>当前是否有请求正在进行</summary>
    public bool IsBusy { get; private set; }

    private readonly ConcurrentQueue<byte[]> chunks = new();
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder buffer = new();
    private readonly StringBuilder rawBody = new();
    private readonly char[] chars = new char[8192];

    private Action<string> onChunk;
    private Action onComplete;

    /// <summary>收到 AI 使用统计事件（type=usage）时触发</summary>
    public event Action<AiUsage> OnAIUsageReceived;


    public static event Action OnPanelOpened;

    public static event Action OnPanelClosed;
    public static bool IsPanelOpen { get; private set; }

    private UnityWebRequest activeRequest;
    private Action pendingRequest;
    private object activeOwner;
    private object pendingOwner;
    private bool stopped;
    private bool completed;

#if UNITY_WEBGL && !UNITY_EDITOR
    private bool streamCompleted;
    private string streamError;
#endif

    [Serializable]
    private class AIConfig
    {
        public string aiUrl;
        public string currentExperimentKey;
    }

    [Serializable]
    private class StreamResponse
    {
        public string type;
        public AiUsage usage;
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
    private class ErrorResponse
    {
        public string detail;
    }

    [Serializable]
    private class ChatRequestBody
    {
        public string message;
    }

    [Serializable]
    private class ExperimentQARequestBody
    {
        public string experiment_key;
        public string question;
    }

    private void Awake()
    {
        Instance = this;
        StartCoroutine(LoadConfig());
    }

    private void OnEnable()
    {
        IsPanelOpen = true;
        OnPanelOpened?.Invoke();
    }

    private void OnDisable()
    {
        IsPanelOpen = false;
        OnPanelClosed?.Invoke();
    }

    private IEnumerator LoadConfig()
    {
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);

        using var request = UnityWebRequest.Get(path);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"加载配置失败: {request.error}\n路径: {path}");
            yield break;
        }

        ParseConfig(request.downloadHandler.text);
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

            currentExperimentKey = config.currentExperimentKey;

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

    #region 对外接口

    /// <summary>
    /// 实验分析: 直接传入 JSON 字符串
    /// </summary>
    public string SendAnalyze(string json, Action<string> chunkCallback, Action completeCallback = null, Action onStart = null, object owner = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail("实验数据为空");

        if (string.IsNullOrEmpty(analyzeUrl))
            return Fail("analyzeUrl 未配置，请检查 StreamingAssets/AIConfig.json");

        Enqueue(() => StartCoroutine(SendRequest(analyzeUrl, json, chunkCallback, completeCallback, onStart)), owner);
        return null;
    }

    /// <summary>
    /// 普通聊天
    /// </summary>
    public string SendChat(string message, Action<string> chunkCallback, Action completeCallback = null, Action onStart = null, object owner = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Fail("消息为空");

        if (string.IsNullOrEmpty(chatUrl))
            return Fail("chatUrl 未配置，请检查 StreamingAssets/AIConfig.json");

        var body = new ChatRequestBody { message = message };
        string json = JsonUtility.ToJson(body);
        Enqueue(() => StartCoroutine(SendRequest(chatUrl, json, chunkCallback, completeCallback, onStart)), owner);
        return null;
    }

    /// <summary>
    /// 实验知识问答：指定实验简称，基于知识库回答
    /// </summary>
    public string SendExperimentQA(string question, Action<string> chunkCallback, Action completeCallback = null, Action onStart = null, object owner = null)
    {
        if (string.IsNullOrWhiteSpace(question))
            return Fail("问题为空");

        if (string.IsNullOrEmpty(experimentQAUrl))
            return Fail("experimentQAUrl 未配置，请检查 StreamingAssets/AIConfig.json");

        var body = new ExperimentQARequestBody
        {
            experiment_key = currentExperimentKey,
            question = question
        };

        Enqueue(() => StartCoroutine(SendRequest(experimentQAUrl, JsonUtility.ToJson(body), chunkCallback, completeCallback, onStart)), owner);
        return null;
    }

    public void Cancel(object owner)
    {
        if (ReferenceEquals(pendingOwner, owner))
        {
            pendingRequest = null;
            pendingOwner = null;
        }

        if (ReferenceEquals(activeOwner, owner))
            StopInternal();
    }

    private static string Fail(string message)
    {
        Debug.LogError(message);
        return message;
    }

    /// <summary>
    /// 只中止当前请求，保留待发请求（抢占时使用）
    /// </summary>
    private bool StopInternal()
    {
        if (completed) return false;
        stopped = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        AIClientStreamAbort();
#else
        if (activeRequest != null) activeRequest.Abort();
#endif
        return true;
    }

    /// <summary>
    /// 唯一请求入口：空闲直接发；忙碌则挂起并抢占
    /// </summary>
    private void Enqueue(Action startRequest, object owner)
    {
        if (IsBusy)
        {
            pendingRequest = startRequest;
            pendingOwner = owner;
            StopInternal();
            return;
        }

        activeOwner = owner;
        startRequest();
    }

    #endregion

    #region 流式请求

    private IEnumerator SendRequest(string url, string jsonBody, Action<string> chunkCallback, Action completeCallback, Action onStart)
    {
        IsBusy = true;
        stopped = false;
        completed = false;
#if UNITY_WEBGL && !UNITY_EDITOR
        streamCompleted = false;
        streamError = null;
#endif

        chunks.Clear();
        buffer.Clear();
        rawBody.Clear();
        decoder.Reset();
        onChunk = chunkCallback;
        onComplete = completeCallback;

        // 只有真正轮到本请求时，才初始化渲染目标
        onStart?.Invoke();

#if UNITY_WEBGL && !UNITY_EDITOR
        // 浏览器：JS XHR 增量读取，数据通过 OnStreamChunk 推回共享管道
        AIClientStreamBegin(gameObject.name, url, jsonBody);
        while (!streamCompleted && !stopped)
        {
            yield return null;
        }

        if (stopped)
        {
            chunks.Clear();
        }
        else if (streamError != null)
        {
            Debug.LogError($"请求失败：{streamError}\n{rawBody}");
            onChunk?.Invoke($"\n\n**请求失败**：{streamError}");
        }
#else
        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new StreamHandler(chunks);
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 300;
        activeRequest = request;

        var operation = request.SendWebRequest();
        while (!operation.isDone && !stopped)
        {
            ProcessChunks();
            yield return null;
        }

        if (stopped)
        {
            // Abort 后队列里可能有残留，清掉避免继续冒字
            chunks.Clear();
        }
        else
        {
            ProcessChunks();

            if (buffer.Length > 0)
            {
                ProcessLine(buffer.ToString().TrimEnd('\r'));
                buffer.Clear();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                string message = BuildErrorMessage(request);
                Debug.LogError($"请求失败：{request.responseCode} {request.error}\n{rawBody}");
                onChunk?.Invoke($"\n\n**请求失败**：{message}");
            }
        }
#endif

        activeRequest = null;
        activeOwner = null;
        IsBusy = false;

        // ① 先让旧面板收尾（渲染目标仍是它自己的）
        if (!completed)
        {
            completed = true;
            onComplete?.Invoke();
        }

        // ② 再启动抢占的请求
        if (pendingRequest != null)
        {
            var next = pendingRequest;
            pendingRequest = null;
            activeOwner = pendingOwner;
            pendingOwner = null;
            next();
        }
    }

    private void ProcessChunks()
    {
        while (chunks.TryDequeue(out byte[] data))
        {
            int count = decoder.GetChars(data, 0, data.Length, chars, 0, false);
            FeedLineData(new string(chars, 0, count));
        }
    }

    /// <summary>
    /// 共享管道：原生解码结果与 WebGL JS 回调都从这里灌入，统一拆行解析
    /// </summary>
    private void FeedLineData(string text)
    {
        buffer.Append(text);
        rawBody.Append(text);

        while (true)
        {
            string current = buffer.ToString();
            int newline = current.IndexOf('\n');
            if (newline < 0) break;

            string line = current.Substring(0, newline).TrimEnd('\r');
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

            if (response != null && response.type == "usage" && response.usage != null)
            {
                AIUsageTracker.Record(response.usage);
                OnAIUsageReceived?.Invoke(response.usage);
                Debug.Log($"[AIUsage] {response.usage.scenario} in={response.usage.input_tokens} out={response.usage.output_tokens} count={AIUsageTracker.ai_data.ai_call_count} \n" +
                    $"ai_input_token_count={AIUsageTracker.ai_data.ai_input_token_count}  ai_output_token_count={AIUsageTracker.ai_data.ai_output_token_count}");
                return;
            }

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

#if UNITY_WEBGL && !UNITY_EDITOR
    // 被 .jslib 的 SendMessage 调用
    public void OnStreamChunk(string text) => FeedLineData(text);
    public void OnStreamComplete() => streamCompleted = true;
    public void OnStreamError(string info) { streamError = info; streamCompleted = true; }
#endif

    [ContextMenu("Dump AIUsage")]
    private void DumpAIUsage()
    {
        Debug.Log(JsonUtility.ToJson(AIUsageTracker.ai_data));
    }

    private string BuildErrorMessage(UnityWebRequest request)
    {
        string prefix = request.responseCode > 0
            ? $"HTTP {request.responseCode}"
            : "网络异常";

        string body = rawBody.ToString().Trim();
        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(body);
                if (error != null && !string.IsNullOrEmpty(error.detail))
                {
                    return $"{prefix}：{error.detail}";
                }
            }
            catch (Exception)
            {
            }
        }

        if (!string.IsNullOrEmpty(request.error))
        {
            return $"{prefix}：{request.error}";
        }

        return prefix;
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