using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>ilab 上报测试辅助（仅测试用，正式发布前建议移除）。</summary>
public class IlabTestHelper : MonoBehaviour
{
    [Header("测试快捷键")]
    [Tooltip("Q：自动填充测试步骤与 AI 统计")]
    public KeyCode fillKey = KeyCode.Q;
    [Tooltip("W：Submit(1) 提交到 Java data_upload 接口")]
    public KeyCode submitKey = KeyCode.W;
    [Tooltip("E：生成 ilab 上报 JSON 并打印到控制台，复制到 Apifox 测试")]
    public KeyCode dumpKey = KeyCode.E;
    [Tooltip("R：读取 StreamingAssets/Test 下 PDF，上传到 attachmentUpload 接口（需先按 W 成功）")]
    public KeyCode uploadKey = KeyCode.R;

    [Header("测试数据配置（仅 Editor/原生下生效）")]
    [Tooltip("测试用 originId；WebGL 下自动用页面 URL 真实值，此字段不起作用")]
    public string testOriginId = "6b05e279_3c00_4706_a6eb_6added601fded";
    [Tooltip("测试用 expId；WebGL 下自动用页面 URL 真实值，此字段不起作用")]
    public string testExpId = "17";

    [Header("附件上传测试数据（Editor 下生效）")]
    [Tooltip("附件相对 StreamingAssets 的路径")]
    public string testAttachmentRelPath = "Test/山东大学实践报告_理论明细版.pdf";
    [Tooltip("附件上传 title 假数据")]
    public string testAttachmentTitle = "山东大学实践报告（理论明细版）";
    [Tooltip("附件上传 remarks 假数据")]
    public string testAttachmentRemarks = "ilab 附件上传测试";

    void Update()
    {
        if (Input.GetKeyDown(fillKey))
            FillDummyData();
        if (Input.GetKeyDown(submitKey))
            SubmitDummy();
        if (Input.GetKeyDown(dumpKey))
            DumpJson();
        if (Input.GetKeyDown(uploadKey))
            UploadDummyAttachment();
    }

    /// <summary>唯一假步骤数据源（Q 填数据 / E 导出 JSON 共用，数据保持一致）。</summary>
    static List<DummyStep> BuildDummySteps()
    {
        var list = new List<DummyStep>
        {
            new DummyStep("步骤1：初始化", 8, "良", UnityEngine.Random.Range(30, 91)),
            new DummyStep("步骤2：配置 I/O", 10, "优", UnityEngine.Random.Range(60, 181)),
        };
        for (int i = 3; i <= 10; i++)
            list.Add(new DummyStep($"步骤{i}", 10, "完成", UnityEngine.Random.Range(20, 121)));
        return list;
    }

    /// <summary>假步骤数据载体。</summary>
    struct DummyStep
    {
        public string title;
        public int score;
        public string eval;
        public int expectTime;
        public DummyStep(string t, int s, string e, int et) { title = t; score = s; eval = e; expectTime = et; }
    }

    void FillDummyData()
    {
        var reporter = IlabExpReporter.Instance;
        if (reporter == null)
        {
            Debug.LogError("[IlabTestHelper] 未找到 IlabExpReporter.Instance，请先在场景中挂载 IlabExpReporter 组件");
            return;
        }

        reporter.ResetExperiment(false);   // Q 键只清步骤，不清 AI 统计（跑完 AI 再按 Q/W，ai_data 保留）

        foreach (var ds in BuildDummySteps())
        {
            var s = reporter.StartStep(ds.title, "赋分模型", maxScore: 10, expectTime: ds.expectTime);
            s.FinishStep(ds.score, ds.eval);
            s.timeUsed = ds.expectTime;
            s.endTime = s.startTime + ds.expectTime * 1000L;
        }

        Debug.Log($"[IlabTestHelper] 已填充 {reporter.Steps.Count} 条测试步骤，可按 W 提交");
    }

    /// <summary>
    /// originId/expId 分流：WebGL 用页面 URL 真实值（Reporter.Awake 原样截取），
    /// Editor/原生下页面 URL 为空，用面板 testOriginId/testExpId。
    /// </summary>
    void ResolveIds(out string originId, out string expId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var reporter = IlabExpReporter.Instance;
        originId = reporter != null ? reporter.OriginId : "";
        expId = reporter != null ? reporter.ExpId : "";
#else
        originId = testOriginId;
        expId = testExpId;
#endif
    }

    void SubmitDummy()
    {
        var reporter = IlabExpReporter.Instance;
        if (reporter == null)
        {
            Debug.LogError("[IlabTestHelper] 未找到 IlabExpReporter.Instance");
            return;
        }

        ResolveIds(out var originId, out var expId);
        reporter.Submit(1, (ok, msg) => Debug.Log($"[IlabTestHelper] dataUpload{(ok ? "成功" : "失败")}：{msg}"),
            originId, expId);   // WebGL 用页面 URL 值，Editor 用面板测试值
    }

    void DumpJson()
    {
        var expSteps = new List<ExpStep>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int i = 1;
        foreach (var ds in BuildDummySteps())
        {
            expSteps.Add(new ExpStep
            {
                seq = i++,
                title = ds.title,
                startTime = now,
                endTime = now + ds.expectTime * 1000,
                timeUsed = ds.expectTime,
                expectTime = ds.expectTime,
                maxScore = 10,
                score = ds.score,
                repeatCount = 1,
                evaluation = ds.eval,
                scoringModel = "赋分模型",
                remarks = $"测试步骤{i - 1}备注"
            });
        }

        var aiData = AIUsageTracker.ai_data;

        ResolveIds(out var originId, out var expId);   // WebGL 导出页面 URL 真实值，Editor 导出测试值

        var data = new ExpData
        {
            originId = originId,
            expId = expId,
            steps = expSteps,
            ai_data = aiData,
            status = 1
        };

        string json = JsonUtility.ToJson(data, true);

        Debug.Log($"[IlabTestHelper] ===== 生成的 JSON（复制到 Apifox）=====\n{json}\n[IlabTestHelper] ===== 结束 =====");
    }

    /// <summary>
    /// R 键：下载测试附件字节流并上传到 attachmentUpload（统一走 UnityWebRequest——
    /// WebGL/Android 下 Application.streamingAssetsPath 是 URL，System.IO 的 File API 不可用）。
    /// </summary>
    void UploadDummyAttachment()
    {
        var reporter = IlabExpReporter.Instance;
        if (reporter == null)
        {
            Debug.LogError("[IlabTestHelper] 未找到 IlabExpReporter.Instance，请先在场景中挂载 IlabExpReporter 组件");
            return;
        }

        string url = Path.Combine(Application.streamingAssetsPath, testAttachmentRelPath).Replace('\\', '/');
        StartCoroutine(DownloadAttachmentBytes(url, bytes =>
        {
            if (bytes == null || bytes.Length == 0)
                return;   // 下载失败已在协程里打日志

            ResolveIds(out var originId, out var expId);
            reporter.UploadAttachment(Path.GetFileName(testAttachmentRelPath), testAttachmentTitle, testAttachmentRemarks, bytes,
                (ok, msg) => Debug.Log($"[IlabTestHelper] 附件上传{(ok ? "成功" : "失败")}：{msg}"), originId, expId);
        }));
    }

    /// <summary>下载附件字节流（File/URL 通用）；失败时打日志并以 null 回调。</summary>
    IEnumerator DownloadAttachmentBytes(string url, Action<byte[]> onDone)
    {
        using var request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[IlabTestHelper] 附件下载失败：{request.responseCode} {request.error}\n{url}");
            onDone(null);
            yield break;
        }

        onDone(request.downloadHandler.data);
    }
}
