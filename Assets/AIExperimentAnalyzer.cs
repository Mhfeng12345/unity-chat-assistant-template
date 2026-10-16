using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AIExperimentAIAnalyzer : MonoBehaviour
{

    [SerializeField] private string ApiUrl = "http://127.0.0.1:18741/analyze";

    [TextArea(5, 15)]
    [SerializeField] private string experimentDataJson;

    private readonly ConcurrentQueue<byte[]> chunks = new();
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder buffer = new();
    private readonly StringBuilder rawBody = new();
    private readonly char[] chars = new char[8192];

    private Action<string> onChunk;
    private Action onComplete;

    [Serializable]
    private class StreamResponse { public StreamChoice[] choices; }

    [Serializable]
    private class StreamChoice { public Delta delta; }

    [Serializable]
    private class Delta { public string content; }


    public static AIExperimentAIAnalyzer Instance;
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

    public void SendAIAnalyze(Action<string> chunkCallback, Action completeCallback = null)
    {
        StartCoroutine(SendRequest(chunkCallback, completeCallback));
    }


    private IEnumerator SendRequest(Action<string> chunkCallback, Action completeCallback)
    {
        chunks.Clear();
        buffer.Clear();
        rawBody.Clear();
        decoder.Reset();

        onChunk = chunkCallback;
        onComplete = completeCallback;

        using var request = new UnityWebRequest(ApiUrl, "POST");
        string json = experimentDataJson;
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
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
            // 422 等错误响应不是 SSE，会被 ProcessLine 丢弃，这里把原始响应体打出来定位。
            Debug.LogError($"实验分析请求失败：{request.responseCode} {request.error}\n{rawBody}");
        }

        onComplete?.Invoke();
    }

    private void ProcessChunks()
    {
        while (chunks.TryDequeue(out byte[] data))
        {
            int count = decoder.GetChars(data, 0, data.Length, chars, 0, false);
            var text = new string(chars, 0, count);
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
}