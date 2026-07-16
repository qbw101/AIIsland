using System.Collections.Generic;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 推荐配置模式中的 API 平台预设数据。
/// </summary>
public record ApiProviderPreset(
    int Index,
    string Name,
    string Model,
    string Endpoint,
    string ConsoleUrl,
    string Badge
)
{
    public static readonly List<ApiProviderPreset> All = new()
    {
        new(1, "阿里百炼",           "deepseek-v4-flash",              "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",         "https://bailian.console.aliyun.com/",             "14+可注册"),
        new(2, "DeepSeek",           "deepseek-v4-flash",              "https://api.deepseek.com/v1/chat/completions",                                  "https://platform.deepseek.com/",                    "高性价比"),
        new(3, "智谱AI",             "glm-4.7-flash",                  "https://open.bigmodel.cn/api/paas/v4/chat/completions",                       "https://open.bigmodel.cn/",                         "送2000万Token"),
        new(4, "月之暗面 (Kimi)",    "moonshot-v1-8k",                 "https://api.moonshot.cn/v1/chat/completions",                                   "https://platform.kimi.com/",                        "256K超长上下文"),
        new(5, "小米MiMo",           "mimo-v2.5-pro",                  "https://api.xiaomimimo.com/v1/chat/completions",                                  "https://platform.xiaomimimo.com",                 "14+可注册"),
        new(6, "火山引擎 (豆包)",    "doubao-seed-2-0-mini-260428",    "https://ark.cn-beijing.volces.com/api/v3/chat/completions",                     "https://console.volcengine.com/ark",                "价格低"),
        new(7, "MiniMax",            "MiniMax-M2.7-highspeed",          "https://api.minimax.chat/v1/text/chatcompletion_v2",                            "https://platform.minimaxi.com/",                    "1M超长上下文"),
    };
}
