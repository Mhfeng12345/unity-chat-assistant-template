using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LLMChat : MonoBehaviour
{
    [SerializeField] private string ApiUrl = "http://192.168.0.50:1234/v1/chat/completions";
    [SerializeField] private string apiKey = "api";
    [SerializeField] private string model = "qwen/qwen3-vl-4b";


    private readonly ConcurrentQueue<byte[]> chunks = new();
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder buffer = new();
    private readonly char[] chars = new char[8192];

    private Action<string> onChunk;
    private Action onComplete;

    [Serializable]
    private class ChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public bool stream = true;
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

    public static LLMChat Instance;

    private void Awake()
    {
        Instance = this;
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

    public void SendAIMessage(string prompt, Action<string> onChunk, Action onComplete = null)
    {
        StartCoroutine(SendRequest(prompt, onChunk, onComplete));
    }

    private IEnumerator SendRequest(string prompt, Action<string> chunkCallback, Action completeCallback)
    {
        chunks.Clear();
        buffer.Clear();
        decoder.Reset();

        this.onChunk = chunkCallback;
        onComplete = completeCallback;

        ChatRequest data = new ChatRequest
        {
            model = model,
            messages = new[]
            {
                new ChatMessage { role = "user", content = prompt }
            }
        };

        using var request = new UnityWebRequest(ApiUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
        request.downloadHandler = new StreamHandler(chunks);
        request.SetRequestHeader("Content-Type", "application/json");

        if (!string.IsNullOrEmpty(apiKey))
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        request.timeout = 180;

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
            Debug.LogError($"LLM 请求失败：{request.responseCode} {request.error}");

        onComplete?.Invoke();
    }

    private void ProcessChunks()
    {
        while (chunks.TryDequeue(out byte[] data))
        {
            int count = decoder.GetChars(data, 0, data.Length, chars, 0, false);
            buffer.Append(chars, 0, count);
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
}