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
        new(1,  "阿里百炼",             "qwen3.6-flash",     "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",            "https://bailian.console.aliyun.com/",               "14+可注册"),
        new(2,  "DeepSeek",             "deepseek-chat",      "https://api.deepseek.com/v1/chat/completions",                                   "https://platform.deepseek.com/",                    "高性价比"),
        new(3,  "智谱 AI",              "glm-4-flash",        "https://open.bigmodel.cn/api/paas/v4/chat/completions",                         "https://open.bigmodel.cn/",                         "送2000万Token"),
        new(4,  "月之暗面 (Kimi)",      "moonshot-v1-8k",     "https://api.moonshot.cn/v1/chat/completions",                                    "https://platform.kimi.com/",                        "256K超长上下文"),
        new(5,  "百度千帆",             "ernie-4.0-turbo-8k", "https://qianfan.baidubce.com/v2/chat/completions",                               "https://console.bce.baidu.com/qianfan",             "中文强"),
        new(6,  "火山引擎 (豆包)",      "doubao-pro-32k",     "https://ark.cn-beijing.volces.com/api/v3/chat/completions",                      "https://console.volcengine.com/ark",                "价格低"),
        new(7,  "腾讯混元",             "hunyuan-lite",       "https://api.hunyuan.cloud.tencent.com/v1/chat/completions",                      "https://console.cloud.tencent.com/hunyuan",         "有免费额度"),
        new(8,  "科大讯飞",             "spark-lite",         "https://spark-api-open.xf-yun.com/v1/chat/completions",                          "https://console.xfyun.cn/",                         "送百万Token"),
        new(9,  "MiniMax",              "abab6.5s-chat",      "https://api.minimax.chat/v1/text/chatcompletion_v2",                             "https://platform.minimaxi.com/",                    "1M超长上下文"),
        new(10, "小米 MiMo",            "mimo-v2.5",          "https://api.xiaomimimo.com/v1/chat/completions",                                 "https://platform.xiaomimimo.com",                   "14+可注册"),
        new(11, "华为盘古",             "pangu-chat",         "https://maas.cn-east-2.myhuaweicloud.com/v1/chat/completions",                   "https://console.huaweicloud.com/modelarts/",        "送100万Token"),
    };
}
