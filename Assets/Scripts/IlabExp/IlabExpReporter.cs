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

/// <summary>交给 Java 的实验数据：steps + ai_data + status（JsonUtility 需要对象根）。</summary>
[Serializable]
public class ExpData
{
    public List<ExpStep> steps;
    public AIUsageTracker.AiData ai_data;
    public int status; // 1=已完成，2=未完成（对齐实验空间 data_upload）
}

/// <summary>
/// 实验空间（ilab-x）对接入口：挂在场景中专门的 GameObject 上（切场景不删）。
/// 记录实验步骤、累计 AI 统计，提交时产出 {"steps":[...],"ai_data":{...},"status":N}，
/// 并 POST 到 Java 提供的接口（地址见 StreamingAssets/config.json 的 serverUrl）。
/// <code>
/// IlabExpReporter.Instance.ResetExperiment();                       // 实验开始
/// var step = IlabExpReporter.Instance.StartStep("装夹工件", "按操作规范赋分"); // 进入步骤
/// step.FinishStep(8, "操作规范");                                    // 完成步骤
/// IlabExpReporter.Instance.Submit(1);                               // 提交（1=已完成，2=未完成）
/// </code>
/// </summary>
public class IlabExpReporter : MonoBehaviour
{
    public static IlabExpReporter Instance { get; private set; }

    [Header("实验空间实验状态：1=已完成，2=未完成")]
    [Tooltip("当前实验状态；提交时也可用 Submit(int)/GetExpData(int) 覆盖")]
    public int status = 2;

    [Header("Java 上传接口")]
    [Tooltip("Java 提供的 data_upload 对接地址；优先用 StreamingAssets/config.json 的 serverUrl 覆盖")]
    public string serverUrl = "";

    [Tooltip("StreamingAssets 下的配置文件名")]
    [SerializeField] private string configFileName = "config.json";

    /// <summary>Java 通过页面 URL 传入的 token（WebGL 下 Awake 解析，回传时拼到 URL）。</summary>
    private string _token = "";

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
        // Java 通过页面 URL 传 token：http://host/index.html?token=xxx
        _token = GetUrlParam(Application.absoluteURL, "token");
        if (!string.IsNullOrEmpty(_token))
            Debug.Log($"[IlabExpReporter] 已从 URL 获取 token={_token}");
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
    /// 取 URL 查询串参数值（Unity 无内置解析，最小实现）。
    /// 约定：token 由 Java 端按标准 URL 编码拼 query（+ 编为 %2B）；
    /// UnEscapeURL 不把 + 当空格，故不会破坏 base64 的 +。
    /// </summary>
    static string GetUrlParam(string url, string key)
    {
        int q = url.IndexOf('?');
        if (q < 0) return "";
        string query = url.Substring(q + 1);
        int hash = query.IndexOf('#');          // fragment 起，后面是前端路由
        if (hash >= 0) query = query.Substring(0, hash);
        foreach (string pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && pair.Substring(0, eq) == key)
                return UnityWebRequest.UnEscapeURL(pair.Substring(eq + 1));
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

    /// <summary>组装交给 Java 的 JSON：{"steps":[...],"ai_data":{...},"status":N}。</summary>
    public string GetExpData(int status)
    {
        for (int i = 0; i < steps.Count; i++)
            steps[i].seq = i + 1;

        return JsonUtility.ToJson(new ExpData
        {
            steps = steps,
            ai_data = AIUsageTracker.ai_data,
            status = status
        });
    }

    /// <summary>用组件上当前 status 取 JSON。</summary>
    public string GetExpData() => GetExpData(status);

    /// <summary>设置当前实验状态（1=已完成，2=未完成）。</summary>
    public void SetStatus(int s) => status = s;

    /// <summary>提交：按指定状态产出 JSON 并 POST 到 Java 接口。</summary>
    public void Submit(int status, Action<bool, string> onDone = null)
    {
        if (string.IsNullOrEmpty(serverUrl))
        {
            Debug.LogError("[IlabExpReporter] serverUrl 未配置，无法上传");
            onDone?.Invoke(false, "serverUrl 未配置");
            return;
        }

        string url = string.IsNullOrEmpty(_token)
            ? serverUrl
            : serverUrl + (serverUrl.Contains("?") ? "&" : "?") + "token=" + UnityWebRequest.EscapeURL(_token);
        StartCoroutine(PostJson(url, GetExpData(status), onDone));
    }

    /// <summary>用组件上当前 status 提交。</summary>
    public void Submit(Action<bool, string> onDone = null) => Submit(status, onDone);

    IEnumerator PostJson(string url, string jsonBody, Action<bool, string> onDone)
    {
        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
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
}
