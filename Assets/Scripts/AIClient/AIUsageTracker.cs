using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>单次 AI 调用 usage 事件（后端 SSE type=usage 解析用）。</summary>
[Serializable]
public class AiUsage
{
    public int input_tokens, output_tokens;
    public string model, version, deployment, provider, scenario;
    public int call_count;
    public bool estimated;
}

/// <summary>会话级 AI 统计：实验开始 Reset，AIClient 收到 usage 自动累计，提交时 JsonUtility.ToJson(ai_data)。</summary>
public static class AIUsageTracker
{
    [Serializable]
    public class AiData
    {
        public int ai_call_count;
        public List<AiDataModel> ai_models_used = new List<AiDataModel>();
        public List<string> ai_usage_scenarios = new List<string>();
        public int ai_input_token_count, ai_output_token_count;
        public string ai_remark = "";
    }

    [Serializable]
    public class AiDataModel
    {
        public string model, version, deployment, provider;
    }

    public static AiData ai_data = new AiData();

    public static void Reset() => ai_data = new AiData();

    public static void Record(AiUsage u)
    {
        ai_data.ai_call_count += u.call_count;
        ai_data.ai_input_token_count += u.input_tokens;
        ai_data.ai_output_token_count += u.output_tokens;

        if (!ai_data.ai_usage_scenarios.Contains(u.scenario))
            ai_data.ai_usage_scenarios.Add(u.scenario);

        if (!ai_data.ai_models_used.Exists(m => m.model == u.model))
            ai_data.ai_models_used.Add(new AiDataModel
            {
                model = u.model,
                version = u.version,
                deployment = u.deployment,
                provider = u.provider
            });

        if (u.estimated)
            ai_data.ai_remark = "部分Token为估算值";
    }
}
