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

        // 步骤 1
        var s1 = reporter.StartStep("步骤1：初始化", "赋分模型", maxScore: 10, expectTime: 60);
        s1.FinishStep(8, "良");

        // 步骤 2
        var s2 = reporter.StartStep("步骤2：配置 I/O", "赋分模型", maxScore: 10, expectTime: 120);
        s2.FinishStep(10, "优");

        // 步骤 3~10（共 10 条，满足 Java 接口 status=1 对步骤数量的要求）
        for (int i = 3; i <= 10; i++)
        {
            var s = reporter.StartStep($"步骤{i}", "赋分模型", maxScore: 10, expectTime: 30);
            s.FinishStep(10, "完成");
        }

        Debug.Log($"[IlabTestHelper] 已填充 {reporter.Steps.Count} 条测试步骤，可按 W 提交");
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

        // 拼接成与 Java 接口完全一致的 JSON body
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
