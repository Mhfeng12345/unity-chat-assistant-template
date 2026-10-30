using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>单条实验步骤，字段名与实验空间接口一致。</summary>
[Serializable]
public class ExpStep
{
    public int seq;
    public string title;
    public long startTime;
    public long endTime;
    public int timeUsed;
    public int expectTime;
    public int maxScore;
    public int score;
    public int repeatCount = 1;
    public string evaluation = "";
    public string scoringModel = "";
    public string remarks = "";

    /// <summary>结束计时，写入得分与评价。</summary>
    public void FinishStep(int score, string evaluation)
    {
        this.score = score;
        this.evaluation = evaluation;
        endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        timeUsed = Math.Max(1, (int)((endTime - startTime) / 1000));
    }
}

/// <summary>交给 Java 的实验数据：originId + expId + steps + ai_data + status（JsonUtility 需要对象根）。</summary>
[Serializable]
public class ExpData
{
    public string originId;   // 页面 URL 中 originId= 后面的值，放入 JSON body
    public string expId;      // 页面 URL 中 expId= 后面的值，放入 JSON body
    public List<ExpStep> steps;
    public AIUsageTracker.AiData ai_data;
    public int status; // 1=已完成，2=未完成（对齐实验空间 data_upload）
}

/// <summary>
/// 实验空间（ilab-x）对接入口：挂在场景中专门的 GameObject 上（切场景不删）。
/// 记录实验步骤、累计 AI 统计，提交时产出 {"originId":…,"expId":…,"steps":[…],"ai_data":{…},"status":N}，
/// 并 POST 到 Java 提供的接口（serverUrl + /api/archive/dataUpload，地址见 StreamingAssets/config.json 的 serverUrl）。
/// 此外支持附件上传接口 /api/archive/attachmentUpload（multipart/form-data）。
/// <code>
/// IlabExpReporter.Instance.ResetExperiment();                       // 实验开始
/// var step = IlabExpReporter.Instance.StartStep("装夹工件", "按操作规范赋分"); // 进入步骤
/// step.FinishStep(8, "操作规范");                                    // 完成步骤
/// IlabExpReporter.Instance.Submit(1);                               // 提交（1=已完成，2=未完成）
/// IlabExpReporter.Instance.UploadAttachment("报告.pdf", "测试报告", "111111233", pdfBytes); // 上传附件
/// </code>
/// </summary>
public class IlabExpReporter : MonoBehaviour
{
    public static IlabExpReporter Instance { get; private set; }

    [Header("实验空间实验状态：1=已完成，2=未完成")]
    [Tooltip("当前实验状态；提交时也可用 Submit(int)/GetExpData(int) 覆盖")]
    public int status = 2;

    [Header("Java 上传接口")]
    [Tooltip("Java 提供的 data_upload 基础地址；代码会拼接 /api/archive/dataUpload 或 /api/archive/attachmentUpload；优先被 StreamingAssets/config.json 的 serverUrl 覆盖")]
    public string serverUrl = "";

    [Tooltip("StreamingAssets 下的配置文件名")]
    [SerializeField] private string configFileName = "config.json";

    /// <summary>
    /// Java 页面 URL 中 token= 后面的值（WebGL 下 Awake 原样截取，回传时原样放请求头 token，不解码）。
    /// </summary>
    private string _token = "";
    /// <summary>页面 URL 中 expId= 后面的值（原样，放入 JSON body）。</summary>
    private string _expId = "";
    /// <summary>页面 URL 中 originId= 后面的值（原样，放入 JSON body）。</summary>
    private string _originId = "";

    readonly List<ExpStep> steps = new List<ExpStep>();

    /// <summary>只读查看已记录的步骤（注意 seq 只在取数时赋值）。</summary>
    public IReadOnlyList<ExpStep> Steps => steps;

    [Serializable]
    private class IlabConfig
    {
        public string serverUrl;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

#if UNITY_WEBGL && !UNITY_EDITOR
        // Java 通过页面 URL 传参：https://www.baidu.com/?token=xxx&expId=17&originId=...
        _token = GetQueryParam(Application.absoluteURL, "token");
        _expId = GetQueryParam(Application.absoluteURL, "expId");
        _originId = GetQueryParam(Application.absoluteURL, "originId");
        if (!string.IsNullOrEmpty(_token))
            Debug.Log($"[IlabExpReporter] token={_token} expId={_expId} originId={_originId}");
#endif

        StartCoroutine(LoadConfig());
    }

    IEnumerator LoadConfig()
    {
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);
        using var request = UnityWebRequest.Get(path);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[IlabExpReporter] 读取配置失败：{request.error}\n路径：{path}");
            yield break;
        }

        try
        {
            var config = JsonUtility.FromJson<IlabConfig>(request.downloadHandler.text);
            if (!string.IsNullOrEmpty(config.serverUrl))
                serverUrl = config.serverUrl.TrimEnd('/');
            Debug.Log($"[IlabExpReporter] serverUrl={serverUrl}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[IlabExpReporter] 解析 config.json 失败：{e.Message}");
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>
    /// 取 URL 查询串中 key= 后面的值（原样，不解码；先去掉 #fragment）。
    /// </summary>
    static string GetQueryParam(string url, string key)
    {
        int q = url.IndexOf('?');
        if (q < 0) return "";
        string query = url.Substring(q + 1);
        int hash = query.IndexOf('#');
        if (hash >= 0) query = query.Substring(0, hash);
        foreach (string pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && pair.Substring(0, eq) == key)
                return pair.Substring(eq + 1);   // 原样，不 UnEscapeURL
        }
        return "";
    }
#endif

    /// <summary>实验开始：清空步骤与 AI 统计。</summary>
    public void ResetExperiment()
    {
        steps.Clear();
        AIUsageTracker.Reset();
    }

    /// <summary>进入一步（自动打开始时间），返回该步对象，其余字段可直接改。</summary>
    public ExpStep StartStep(string title, string scoringModel, int maxScore = 100, int expectTime = 0)
    {
        var step = new ExpStep
        {
            title = title,
            startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            maxScore = maxScore,
            expectTime = expectTime,
            scoringModel = scoringModel
        };
        steps.Add(step);
        return step;
    }

    /// <summary>
    /// 组装交给 Java 的 JSON：{"originId":…,"expId":…,"steps":[…],"ai_data":{…},"status":N}。
    /// </summary>
    public string GetExpData(int status)
    {
        for (int i = 0; i < steps.Count; i++)
            steps[i].seq = i + 1;

        return JsonUtility.ToJson(new ExpData
        {
            originId = _originId,
            expId = _expId,
            steps = steps,
            ai_data = AIUsageTracker.ai_data,
            status = status
        });
    }

    /// <summary>提交：按指定状态产出 JSON 并 POST 到 Java data_upload 接口。</summary>
    public void Submit(int status, Action<bool, string> onDone = null)
    {
        if (string.IsNullOrEmpty(serverUrl))
        {
            Debug.LogError("[IlabExpReporter] serverUrl 未配置，无法上传");
            onDone?.Invoke(false, "serverUrl 未配置");
            return;
        }

        string url = serverUrl + "/api/archive/dataUpload";
        StartCoroutine(PostJson(url, GetExpData(status), onDone));
    }

    /// <summary>用组件上当前 status 提交。</summary>
    public void Submit(Action<bool, string> onDone = null) => Submit(status, onDone);

    /// <summary>
    /// 上传附件到 Java attachmentUpload 接口（multipart/form-data）。
    /// originId/expId 取页面 URL 原样值；token 放请求头；fileData 为文件字节流。
    /// </summary>
    public void UploadAttachment(string filename, string title, string remarks, byte[] fileData, Action<bool, string> onDone = null)
    {
        if (string.IsNullOrEmpty(serverUrl))
        {
            Debug.LogError("[IlabExpReporter] serverUrl 未配置，无法上传附件");
            onDone?.Invoke(false, "serverUrl 未配置");
            return;
        }
        if (fileData == null || fileData.Length == 0)
        {
            Debug.LogError("[IlabExpReporter] fileData 为空，无法上传附件");
            onDone?.Invoke(false, "fileData 为空");
            return;
        }

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("originId", _originId),
            new MultipartFormDataSection("expId", _expId),
            new MultipartFormDataSection("filename", filename),
            new MultipartFormDataSection("title", title),
            new MultipartFormDataSection("remarks", remarks),
            new MultipartFormFileSection("file", fileData, filename, "application/pdf")
        };

        StartCoroutine(PostForm(serverUrl + "/api/archive/attachmentUpload", form, onDone));
    }

    IEnumerator PostJson(string url, string jsonBody, Action<bool, string> onDone)
    {
        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrEmpty(_token))
            request.SetRequestHeader("token", _token);   // token 原样放 header（非 WebGL 下恒空，不设）
        request.timeout = 300;
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[IlabExpReporter] 上传成功：{request.responseCode}");
            onDone?.Invoke(true, request.downloadHandler.text);
        }
        else
        {
            Debug.LogError($"[IlabExpReporter] 上传失败：{request.responseCode} {request.error}");
            onDone?.Invoke(false, $"{request.responseCode} {request.error}");
        }
    }

    /// <summary>
    /// 发送 multipart/form-data 表单（附件上传用），token 原样放请求头。
    /// </summary>
    IEnumerator PostForm(string url, List<IMultipartFormSection> form, Action<bool, string> onDone)
    {
        using var request = UnityWebRequest.Post(url, form);
        if (!string.IsNullOrEmpty(_token))
            request.SetRequestHeader("token", _token);   // token 原样放 header（非 WebGL 下恒空，不设）
        request.timeout = 300;
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[IlabExpReporter] 附件上传成功：{request.responseCode}");
            onDone?.Invoke(true, request.downloadHandler.text);
        }
        else
        {
            Debug.LogError($"[IlabExpReporter] 附件上传失败：{request.responseCode} {request.error}");
            onDone?.Invoke(false, $"{request.responseCode} {request.error}");
        }
    }
}
