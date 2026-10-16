using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LLMStreamTest : MonoBehaviour
{
    // 修改为 chat/completions 以匹配 messages 结构
    private const string ApiUrl = "http://192.168.0.50:1234/v1/chat/completions";

    // 本地 LM Studio 可留空，无需真实 Key
    [SerializeField] private string apiKey = "lm-studio";

    private readonly ConcurrentQueue<byte[]> chunks = new();
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder buffer = new();
    private readonly char[] chars = new char[8192];

    private bool isStreaming;

    [Serializable]
    public class ChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class ChatRequest
    {
        public string model = "qwen/qwen3-vl-4b";
        public ChatMessage[] messages;
        public float temperature = 1;
        public float top_p = 1;
        public int max_tokens = 4096;
        public bool stream = true;
    }

    [Serializable]
    public class StreamResponse
    {
        public StreamChoice[] choices;
    }

    [Serializable]
    public class StreamChoice
    {
        public Delta delta;
    }

    [Serializable]
    public class Delta
    {
        public string content;
    }

    private class StreamDownloadHandler : DownloadHandlerScript
    {
        private readonly ConcurrentQueue<byte[]> chunks;

        public StreamDownloadHandler(ConcurrentQueue<byte[]> chunks) : base(new byte[8192])
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

    [ContextMenu("测试本地 LM Studio 流式输出")]
    public void TestChat()
    {
        StartCoroutine(SendMessage("你好，请帮我写一首诗。"));
    }

    public IEnumerator SendMessage(string prompt)
    {
        chunks.Clear();
        buffer.Clear();
        decoder.Reset();
        isStreaming = true;

        ChatRequest requestData = new ChatRequest
        {
            messages = new[]
            {
                new ChatMessage { role = "user", content = prompt }
            }
        };

        string json = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(ApiUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new StreamDownloadHandler(chunks);
            request.SetRequestHeader("Content-Type", "application/json");

            // 本地调用可注释掉 Authorization，若保留也不影响
            if (!string.IsNullOrEmpty(apiKey))
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            request.timeout = 120;

            Debug.Log("AI 开始生成：");

            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                ProcessChunks();
                yield return null;
            }

            ProcessChunks();
            isStreaming = false;

            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogError($"请求失败：{request.responseCode}\n{request.error}");
            else
                Debug.Log("\nAI 回复完成");
        }
    }

    private void Update()
    {
        if (isStreaming) ProcessChunks();
    }

    private void ProcessChunks()
    {
        while (chunks.TryDequeue(out byte[] data))
        {
            int count = decoder.GetChars(data, 0, data.Length, chars, 0, false);
            buffer.Append(chars, 0, count);
        }

        int newline;
        while ((newline = buffer.ToString().IndexOf('\n')) >= 0)
        {
            string line = buffer.ToString(0, newline).TrimEnd('\r');
            buffer.Remove(0, newline + 1);
            ProcessLine(line);
        }
    }

    private void ProcessLine(string line)
    {
        if (!line.StartsWith("data:")) return;

        string data = line.Substring(5).Trim();
        if (data == "[DONE]") return;

        try
        {
            StreamResponse response = JsonUtility.FromJson<StreamResponse>(data);
            string content = response?.choices?[0]?.delta?.content;

            if (!string.IsNullOrEmpty(content))
                Debug.Log(content); // 实际项目中可拼接至 UI 文本
        }
        catch (Exception e)
        {
            Debug.LogWarning($"解析流式数据失败：{e.Message}");
        }
    }
}