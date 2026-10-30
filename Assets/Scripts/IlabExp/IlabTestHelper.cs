using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ilab 上报测试辅助（仅测试用，正式发布前建议移除）。
/// Q：自动填充 10 条测试步骤（分数 0-100，满足 Java status=1 时 10~200 条要求）
/// W：Submit(1) 提交到 Java data_upload 接口
/// E：生成 ilab 上报 JSON 并打印到控制台，复制到 Apifox 测试
/// </summary>
public class IlabTestHelper : MonoBehaviour
{
    [Header("测试快捷键")]
    [Tooltip("Q：自动填充测试步骤与 AI 统计")]
    public KeyCode fillKey = KeyCode.Q;
    [Tooltip("W：Submit(1) 提交到 Java data_upload 接口")]
    public KeyCode submitKey = KeyCode.W;
    [Tooltip("E：生成 JSON 并打印到控制台")]
    public KeyCode dumpKey = KeyCode.E;

    [Header("测试数据配置（Editor 下生效）")]
    [Tooltip("测试用 originId，WebGL 下会优先取页面 URL 中的 originId= 后面的值")]
    public string testOriginId = "6b05e279_3c00_4706_a6eb_6added601fded";
    [Tooltip("测试用 expId，WebGL 下会优先取页面 URL 中的 expId= 后面的值")]
    public string testExpId = "17";

    void Update()
    {
        if (Input.GetKeyDown(fillKey))
            FillDummyData();
        if (Input.GetKeyDown(submitKey))
            SubmitDummy();
        if (Input.GetKeyDown(dumpKey))
            DumpJson();
    }

    void FillDummyData()
    {
        var reporter = IlabExpReporter.Instance;
        if (reporter == null)
        {
            Debug.LogError("[IlabTestHelper] 未找到 IlabExpReporter.Instance，请先在场景中挂载 IlabExpReporter 组件");
            return;
        }

        reporter.ResetExperiment();

        AddStep(reporter, "步骤1：初始化", 8, "良", UnityEngine.Random.Range(30, 91));
        AddStep(reporter, "步骤2：配置 I/O", 10, "优", UnityEngine.Random.Range(60, 181));
        for (int i = 3; i <= 10; i++)
            AddStep(reporter, $"步骤{i}", 10, "完成", UnityEngine.Random.Range(20, 121));

        Debug.Log($"[IlabTestHelper] 已填充 {reporter.Steps.Count} 条测试步骤，可按 W 提交");
    }


    static void AddStep(IlabExpReporter reporter, string title, int score, string eval, int expectTime)
    {
        var s = reporter.StartStep(title, "赋分模型", maxScore: 10, expectTime: expectTime);
        s.FinishStep(score, eval);
        s.timeUsed = expectTime; 
        s.endTime = s.startTime + expectTime * 1000L; 
    }

    void SubmitDummy()
    {
        var reporter = IlabExpReporter.Instance;
        if (reporter == null)
        {
            Debug.LogError("[IlabTestHelper] 未找到 IlabExpReporter.Instance");
            return;
        }

        reporter.Submit(1, (ok, msg) => Debug.Log($"[IlabTestHelper] 提交{(ok ? "成功" : "失败")}：{msg}"));
    }

    void DumpJson()
    {
        var expSteps = new List<ExpStep>();

        // 生成 10 条假步骤（满足 Java 接口 status=1 时 10~200 条的要求）
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 1; i <= 10; i++)
        {
            var step = new ExpStep
            {
                seq = i,
                title = $"测试步骤{i}",
                startTime = now,
                endTime = now + 60000,
                timeUsed = 60,
                expectTime = 60,
                maxScore = 10,
                score = i % 2 == 0 ? 10 : 8,
                repeatCount = 1,
                evaluation = i % 2 == 0 ? "优" : "良",
                scoringModel = "赋分模型",
                remarks = $"测试步骤{i}备注"
            };
            expSteps.Add(step);
        }

        // ai_data：沿用 AIUsageTracker 累计的真实值（跑完 AI 后有值；未跑则为默认全0/空）
        var aiData = AIUsageTracker.ai_data;


        var data = new ExpData
        {
            originId = testOriginId,
            expId = testExpId,
            steps = expSteps,
            ai_data = aiData,
            status = 1
        };

        string json = JsonUtility.ToJson(data, true);

        Debug.Log($"[IlabTestHelper] ===== 生成的 JSON（复制到 Apifox）=====\n{json}\n[IlabTestHelper] ===== 结束 =====");
    }
}
