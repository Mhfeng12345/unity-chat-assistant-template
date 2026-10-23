using System;
using System.Collections.Generic;
using UnityEngine;

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
    public void Finish(int score, string evaluation)
    {
        this.score = score;
        this.evaluation = evaluation;
        endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        timeUsed = Math.Max(1, (int)((endTime - startTime) / 1000));
    }
}

/// <summary>交给后端的实验数据：steps + ai_data（JsonUtility 需要对象根）。</summary>
[Serializable]
public class ExpData
{
    public List<ExpStep> steps;
    public AIUsageTracker.AiData ai_data;
}

/// <summary>
/// 实验空间（ilab-x）实验结果回传接口的数据出口：记录 steps，最后一次性产出
/// {"steps":[...],"ai_data":{...}}，外层 data_upload 由 Java 对接。
/// <code>
/// ExpStepsData.Clear();
/// AIUsageTracker.Reset();                                  // 实验开始时
/// var step = ExpStepsData.Start("装夹工件", "按操作规范赋分");
/// step.Finish(8, "操作规范");                               // 每步结束时
/// string json = ExpStepsData.GetExpData();                 // 提交时
/// </code>
/// </summary>
public static class ExpStepsData
{
    public static readonly List<ExpStep> Steps = new List<ExpStep>();


    /// <summary>开始记录一步（自动打开始时间），返回该步对象，其余字段可直接改。</summary>
    public static ExpStep Start(string title, string scoringModel, int maxScore = 100, int expectTime = 0)
    {
        var step = new ExpStep
        {
            title = title,
            startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            maxScore = maxScore,
            expectTime = expectTime,
            scoringModel = scoringModel
        };

        Steps.Add(step);
        return step;
    }

    /// <summary>组装交给后端的 JSON：{"steps":[...],"ai_data":{...}}。</summary>
    public static string GetExpData()
    {
        for (int i = 0; i < Steps.Count; i++)
            Steps[i].seq = i + 1;

        return JsonUtility.ToJson(new ExpData
        {
            steps = Steps,
            ai_data = AIUsageTracker.ai_data
        });
    }
}
