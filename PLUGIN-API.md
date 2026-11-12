# AIIsland 插件 API 文档

> 对应 AIIsland `1.9.0.0`，ClassIsland API `2.0.0.0`  
> 依据当前 `IAIIslandApi`、`AIIslandApi` 和授权实现整理，更新于 2026-11-07

## 1. API 范围

AIIsland 向其他 ClassIsland 插件注册 `IAIIslandApi` 单例。调用方可以复用用户在 AIIsland 中配置的模型、API 地址、密钥、缓存、重试和提示词，无需再次保存密钥。

当前公开能力包括：

- 通用 AI 对话
- 流式输出（增量回调，含思考过程）
- 深度思考与思考强度控制（按厂商自动适配请求字段），并返回模型思考过程
- 图片识别对话（多模态）
- 自然语言作业解析
- 自然语言提醒解析
- 今日课表总结、学习提示和作业量估算
- 智能每日简报、课间提醒和放学总结的手动触发

这是一套进程内 C# API，不是 HTTP API。调用方必须与 AIIsland 运行在同一个 ClassIsland 进程中。

## 2. 接入

### 2.1 声明插件依赖

调用方应在 `manifest.yml` 中声明 AIIsland 为必需依赖，保证加载顺序和运行时可用性：

```yaml
dependencies:
  - id: ClassIsland.AISmartClass
    isRequired: true
```

### 2.2 添加程序集引用

编译时引用与目标版本一致的 `ClassIsland.AISmartClass.dll`。不要把该 DLL 复制进调用方插件包，运行时应使用用户安装的 AIIsland 程序集。

```xml
<ItemGroup>
  <Reference Include="ClassIsland.AISmartClass">
    <HintPath>path\to\ClassIsland.AISmartClass.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

主要命名空间：

```csharp
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.PublicApi;
using ClassIsland.Shared;
```

### 2.3 获取 API

API 已作为单例注册到 ClassIsland 的依赖注入容器：

```csharp
var api = IAppHost.TryGetService<IAIIslandApi>();
if (api is null)
{
    // AIIsland 未安装、未启用或尚未完成加载。
    return;
}
```

在由 ClassIsland 创建的服务中也可以使用构造函数注入：

```csharp
public sealed class StudyService(IAIIslandApi aiIslandApi)
{
    private readonly IAIIslandApi _ai = aiIslandApi;
}
```

### 2.4 最小示例

```csharp
var api = IAppHost.TryGetService<IAIIslandApi>();
if (api is null || !api.IsConfigured)
    return;

var result = await api.ChatAsync(
    "你是简洁的校园学习助手，只输出一句话。",
    "给出一条整理数学错题的建议。",
    new AIIslandChatOptions
    {
        Description = "生成错题整理建议",
        Temperature = 0.5
    },
    cancellationToken);

if (result.Success)
    UseText(result.Content);
else
    LogWarning(result.Error ?? "AIIsland 调用失败");
```

## 3. 授权行为

`IsConfigured`、`ModelName`、`SupportsVision`、`VisionSupportLevel` 和 `IsVisionSupportVerified` 不需要授权，其余公开方法均经过授权检查。

调用方标识由调用栈中的第一个外部程序集名称推断。例如程序集名为 `My.ClassIsland.Plugin`，授权记录中的 `PluginId` 和当前 `PluginName` 都会是 `My.ClassIsland.Plugin`。当前实现不会读取调用方 `manifest.yml` 的 `id` 或显示名称。

授权模式：

| 模式 | 值 | 行为 |
|---|---:|---|
| `PerCallConfirm` | `0` | 每次调用弹出授权确认，默认模式 |
| `Trusted` | `1` | 直接执行，不再弹窗 |

确认框操作：

| 用户操作 | 结果 |
|---|---|
| 允许 | 仅允许本次调用 |
| 允许并记住 | 允许本次调用，并将程序集设为 `Trusted` |
| 拒绝 | 不调用 AI，返回相应失败结果或状态文本 |

若用户把全局默认策略设为“直接授权”，没有授权记录的程序集会在首次调用时自动加入可信列表。授权记录会保存累计调用次数、最后调用时间和最后调用的方法名。

用户可在 `ClassIsland 设置 -> AIIsland -> 外部插件授权` 中修改或移除记录。

## 4. `IAIIslandApi`

```csharp
public interface IAIIslandApi
{
    bool IsConfigured { get; }
    string? ModelName { get; }
    bool SupportsVision { get; }
    string VisionSupportLevel { get; }
    bool IsVisionSupportVerified { get; }

    Task<AIIslandChatResult> ChatAsync(
        string systemPrompt,
        string userMessage,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default);

    Task<AIIslandChatResult> ChatStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<AIIslandStreamUpdate> onUpdate,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default);

    Task<AIIslandChatResult> ChatWithImageAsync(
        string systemPrompt,
        string userMessage,
        byte[] imageData,
        string? imageMimeType = null,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default);

    Task<AIIslandChatResult> ChatWithImageStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<AIIslandStreamUpdate> onUpdate,
        byte[] imageData,
        string? imageMimeType = null,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default);

    Task<HomeworkParseResult> ParseHomeworkAsync(
        string input,
        CancellationToken ct = default);

    Task<ReminderParseResult> ParseReminderAsync(
        string input,
        CancellationToken ct = default);

    Task<string> SummarizeTodayAsync(
        List<string> subjects,
        CancellationToken ct = default);

    Task<string> GenerateLearningHintAsync(
        List<string> subjects,
        string? focusSubject = null,
        CancellationToken ct = default);

    Task<string> EstimateHomeworkLoadAsync(
        List<string> subjects,
        CancellationToken ct = default);

    Task<string> TriggerBeforeSchoolReminderAsync(
        CancellationToken ct = default);

    Task<string> TriggerBreakReminderAsync(
        CancellationToken ct = default);

    Task<string> TriggerAfterSchoolSummaryAsync(
        CancellationToken ct = default);
}
```

### 4.1 属性

| 属性 | 类型 | 说明 |
|---|---|---|
| `IsConfigured` | `bool` | 当前 API Key 是否非空；不验证地址、模型、余额或网络连通性 |
| `ModelName` | `string?` | AIIsland 当前配置的模型名 |
| `SupportsVision` | `bool` | 当前模型是否被判定为支持图片识别 |
| `VisionSupportLevel` | `string` | `supported` / `unsupported` / `unknown` |
| `IsVisionSupportVerified` | `bool` | 判定是否来自真实检测；`false` 表示按模型名称推断 |

`SupportsVision` 与 `VisionSupportLevel` 由用户在设置页点「检测」后写入，结论绑定当时的「接口地址 + 模型名」；用户换模型或换接口后旧结论自动失效，回退到按名称推断。`IsVisionSupportVerified=true` 时才建议把结论直接展示给用户。

### 4.2 返回行为总览

| 方法 | 成功返回 | 未配置或拒绝授权 | AI/网络失败 |
|---|---|---|---|
| `ChatAsync` | `AIIslandChatResult.Success=true` | `Success=false` | `Success=false`，错误写入 `Error` |
| `ChatStreamAsync` | `Success=true`（增量已通过回调送出） | `Success=false`，回调不会被调用 | `Success=false`；可能已收到部分增量 |
| `ChatWithImageAsync` | `AIIslandChatResult.Success=true` | `Success=false` | `Success=false`，错误写入 `Error`；图片过大或模型已确认不支持时同样返回 `Success=false` |
| `ChatWithImageStreamAsync` | `Success=true`（增量已通过回调送出） | `Success=false`，回调不会被调用 | `Success=false`；可能已收到部分增量 |
| `ParseHomeworkAsync` | `HomeworkParseResult` | `Success=false` | 可能使用本地规则；最终状态见 `Success` 和 `UsedLocalRules` |
| `ParseReminderAsync` | `ReminderParseResult` | `Success=false` | 最终状态见 `Success` 和 `ErrorMessage` |
| 3 个文本生成方法 | 生成文本或本地降级文本 | 中文状态文本 | 中文失败文本或本地降级文本 |
| 3 个提醒触发方法 | 已生成并显示的通知正文 | 中文状态文本 | 中文失败文本 |

字符串返回方法没有独立的成功标志。调用方若需要可靠地区分错误，必须识别 `AIIsland 尚未配置 API Key`、`用户拒绝了授权请求` 和以 `调用失败:` 开头的文本。

所有方法在调用方传入的 `CancellationToken` 被取消时都会继续抛出 `OperationCanceledException`，不会转换成结果对象或状态文本。

## 5. 方法说明

### 5.1 `ChatAsync`

适用于调用方自定义系统提示词和用户消息的场景。

```csharp
var result = await api.ChatAsync(
    systemPrompt: "你是校园广播文案助手。",
    userMessage: "用20字提醒同学带伞。",
    options: new AIIslandChatOptions
    {
        Temperature = 0.4,
        Description = "生成天气提醒",
        BypassCache = false
    },
    ct: cancellationToken);
```

`ChatAsync` 在 API Key 未配置、授权被拒绝、超时、HTTP 错误或响应无法解析时返回 `Success=false`。调用取消除外。

需要更强的推理时，可以按次开启深度思考并指定强度（不传则用设置页的全局开关与强度）：

```csharp
var deep = await api.ChatAsync(
    systemPrompt: "你是数学老师，回答要说明推理过程。",
    userMessage: "为什么 0.999… 等于 1？",
    options: new AIIslandChatOptions
    {
        EnableDeepThinking = true,
        ThinkingEffort = ThinkingEffort.High,
        Description = "解释极限"
    },
    ct: cancellationToken);
```

开启深度思考后，部分厂商会忽略温度参数（例如 DeepSeek 在思考模式下不响应 `temperature`），回复速度与 token 消耗也会明显上升。

开启深度思考后，思考过程会一并返回在 `AIIslandChatResult.Reasoning` 里：

```csharp
var result = await api.ChatAsync(
    "你是数学老师。", "为什么 0.999… 等于 1？",
    new AIIslandChatOptions { EnableDeepThinking = true, ThinkingEffort = ThinkingEffort.High },
    ct);

Console.WriteLine(result.Content);   // 最终回答
Console.WriteLine(result.Reasoning); // 思考过程；未开启深度思考或厂商不返回时为空字符串
```

注意：`Reasoning` 来自厂商的 `reasoning_content` 字段，属于模型内部推理，可能包含不准确的内容；不要把它当作可展示给最终用户的结论，也不要把过往轮次的思考过程回传给接口（DeepSeek 会忽略，部分厂商在带工具调用时反而要求回传）。联网搜索的工具回传完全由 AIIsland 内部管理，调用方无需处理。

### 5.1.1 联网搜索（`EnableWebSearch`）

开启后，模型会在判断问题需要实时信息时自主调用 `web_search` 工具，AIIsland 执行检索并把结果作为资料回传给模型，由模型据此作答。调用方不需要处理工具协议，也不需要自己发起第二次请求。

```csharp
var result = await api.ChatAsync(
    "你是助手。", "今天有什么科技新闻？",
    new AIIslandChatOptions { EnableWebSearch = true, Description = "新闻问答" },
    ct);

Console.WriteLine(result.Content);
if (result.UsedWebSearch)
{
    foreach (var source in result.WebSources ?? Array.Empty<AIIslandWebSource>())
        Console.WriteLine($"[{source.Title}]({source.Url})");
}
```

| 字段 / 行为 | 说明 |
|---|---|
| `EnableWebSearch` | `null`（默认）跟随设置页全局开关；`true` 本次允许联网；`false` 本次强制不联网 |
| `UsedWebSearch` | 模型**实际**执行了联网检索（或打开网页）才为 `true`。模型判断不需要联网时，即使开启也为 `false` |
| `WebSources` | 命中的来源与成功打开的网页（标题 / 地址 / 摘要 / 发布时间）；未联网或没有结果时为 `null` |
| `AIIslandChatResult.SearchQuery` | 本次联网实际使用的检索关键词，多个用 `；` 分隔；未联网时为 `null`。不含打开的网页地址，可直接用于「已按 xxx 搜索」这类说明 |
| `AIIslandStreamUpdate.Status` | 流式专有。`"searching"` 表示正在检索；`"answering"` 表示检索完成、开始生成最终答案，此时应清空此前已显示的内容（第一轮的过渡语会与最终答案重复） |
| `AIIslandStreamUpdate.SearchQuery` | 流式专有，仅 `Status == "searching"` 时可能有值：模型实际使用的检索关键词（多个用 `；` 分隔，最多 3 个），打开网页时则为要访问的 URL。可直接显示为「正在搜索：{SearchQuery}」，为空时退回通用的「正在搜索…」 |

注意事项：

* 联网调用会多一次 HTTP 检索与一轮生成，且本次请求的输出上限与超时会被自动抬高（至少 800 token / 30 秒），耗时与费用都高于普通调用。
* 检索失败不会让调用失败：模型会收到「联网不可用」的说明并如实作答，此时 `UsedWebSearch` 仍为 `true`（确实尝试过），`WebSources` 为空。
* 开启联网后模型实际有两个工具：`web_search` 检索摘要、`web_fetch` 打开网页读正文。后者可在设置页「允许模型打开网页」单独关闭（默认开启），关闭后请求体只声明 `web_search`。
* `web_fetch` 默认在本地抓取，只允许 http/https 的 80/443 端口并拒绝内网地址；页面需要脚本渲染或被反爬拦截时会如实告知模型，不会编造正文，也不会让整次调用失败。开启「DeepSeek 官方联网兜底」时，本地失败会自动改用 DeepSeek 的 `/v1/tools/fetch` 再抓一次，对调用方透明。
* 当前接口不支持 `tools` 时（部分兼容层会返回 400），AIIsland 会自动丢弃工具定义重发一次，本次退化为普通对话，`UsedWebSearch` 为 `false`。
* 部分模型（自托管、OCR/VL 变体，以及沿用 Qwen1/2 老模板的接口）不会返回结构化 `tool_calls`，而是把调用写成正文里的 `✿FUNCTION✿: … ✿ARGS✿: …`。AIIsland 会自动识别这种文本协议并照常执行检索（自 1.9.0.0 起），调用方无需处理，也不会在 `Content` 里看到这些标记。
* 启用联网会把用户问题发送到搜索引擎，授权确认框会附带提示。

### 5.2 `ChatWithImageAsync`（图片识别）

把一张图片和一段文字一起发给模型，返回对图片的回答。图片以 data URI 内联在请求体中，不经过任何图床或第三方存储。

```csharp
var api = IAppHost.TryGetService<IAIIslandApi>();
if (api is null || !api.IsConfigured)
    return;

// 先看能力，避免在纯文本模型上白等一次请求
if (!api.SupportsVision)
{
    var hint = api.IsVisionSupportVerified ? "已确认" : "按名称推断，可在设置页检测确认";
    Console.WriteLine($"当前模型不支持图片识别（{api.VisionSupportLevel}，{hint}）");
    return;
}

var imageBytes = await File.ReadAllBytesAsync("homework.png", cancellationToken);
var result = await api.ChatWithImageAsync(
    systemPrompt: "你在帮中学生看作业照片。只回答看到的内容，不要编造。",
    userMessage: "这张照片里有哪几道题？按题号列出。",
    imageData: imageBytes,
    imageMimeType: "image/png",
    options: new AIIslandChatOptions { Description = "识别作业照片" },
    ct: cancellationToken);

if (result.Success)
    Console.WriteLine(result.Content);
else
    Console.WriteLine(result.Error);
```

约定与限制：

- 支持 PNG / JPEG / GIF / WebP / BMP；`imageMimeType` 留空时按文件头自动识别。
- 单张图片上限 20 MB。建议先缩放到长边 1024～2048 px，既省 token 也更稳。
- 模型已**确认**不支持图片识别时，方法会立刻返回 `Success=false`，不会发出请求；仅按名称推断出的「不支持」不拦截，仍会实际请求一次。
- 厂商因为图片报错时，`Error` 会自动补一句「当前模型可能不支持图片识别」的提示。
- 请求与文本对话共用缓存、重试和调用日志；缓存键包含图片内容的哈希，同一句提问配不同图片不会互相命中。
- 需要逐字渲染时使用流式版本 `ChatWithImageStreamAsync`：签名在 `ChatWithImageAsync` 基础上把 `options` 换成了回调 `onUpdate` + 图片参数，回调结构与 `ChatStreamAsync` 相同。

```csharp
var result = await api.ChatWithImageStreamAsync(
    systemPrompt: "你在帮中学生看作业照片。",
    userMessage: "这张照片里有哪几道题？",
    onUpdate: update => Console.WriteLine(update.Content),
    imageData: imageBytes,
    imageMimeType: "image/png",
    options: new AIIslandChatOptions { Description = "识别作业照片" },
    ct: cancellationToken);
```

### 5.3 `ChatStreamAsync`（流式输出）

与 `ChatAsync` 相同的对话能力，但模型每产出一段增量就回调一次，适合逐字渲染的 UI。回调参数包含本次增量与截至目前的完整快照，正文与思考过程分开携带：

```csharp
var builder = new StringBuilder();
var result = await api.ChatStreamAsync(
    systemPrompt: "你是校园广播文案助手。",
    userMessage: "用20字提醒同学带伞。",
    onUpdate: update =>
    {
        // 注意：回调在线程池线程上执行，更新 UI 需自行切回 UI 线程
        builder.Append(update.ContentDelta);
        Console.WriteLine($"正文: {update.Content} | 思考: {update.Reasoning}");
    },
    options: new AIIslandChatOptions
    {
        EnableDeepThinking = true,
        ThinkingEffort = ThinkingEffort.High,
        Description = "生成天气提醒"
    },
    ct: cancellationToken);

Console.WriteLine(result.Content);   // 最终正文
Console.WriteLine(result.Reasoning); // 最终思考过程
```

行为约定：

- 回调可能收到正文增量（`ContentDelta`）或思考增量（`ReasoningDelta`），同一次推送通常只有一段非空；`Content` / `Reasoning` 是截至目前的完整快照。
- 缓存命中时会按字符回放缓存内容，同样通过回调送出，与真实流式无差别。
- 重试时快照会从头开始，`Content` / `Reasoning` 不会拼接上一次中断的半截内容。
- 回调内抛出的异常会中断整次调用，并作为失败写入 `Error`。
- 取消令牌触发时抛出 `OperationCanceledException`，之前已送出的增量不会撤回。
- 失败降级时回调会收到一段本地降级文案，随后返回 `Success=true` 且 `IsFallback=true` 的结果。

带图片的流式版本是 `ChatWithImageStreamAsync`（见 5.2），增量与快照结构完全相同；已确认不支持图片的模型会直接通过回调收到一段本地降级文案，不发请求。

### 5.4 `ParseHomeworkAsync`

将自然语言作业描述转换为结构化条目：

```csharp
var parsed = await api.ParseHomeworkAsync(
    "数学完成练习册第5页，明天交；后天检查语文背诵",
    cancellationToken);

if (parsed.Success)
{
    foreach (var item in parsed.Items)
    {
        Console.WriteLine($"{item.Subject}: {item.Content}");
        Console.WriteLine($"截止 {item.ParsedDueDate:yyyy-MM-dd}");
    }
}
```

公开 API 会先检查 AIIsland 是否配置了 API Key。未配置时直接返回失败，不会进入本地规则解析；API 已配置但调用过程失败时，内部解析器可能回退到本地规则。

### 5.5 `ParseReminderAsync`

将自然语言提醒解析为 `FixedTime`、`SubjectLinked` 或 `DailyRepeat`：

```csharp
var parsed = await api.ParseReminderAsync(
    "每天早上7点30分提醒我背单词",
    cancellationToken);

if (parsed.Success)
{
    var reminder = parsed.ToCustomReminder();
    // ToCustomReminder 只进行模型转换，不会自动把提醒保存到 AIIsland。
}
```

该公开方法直接调用 AI 解析路径，不会先运行 `ReminderParserService` 的本地正则解析，也不会自动创建或保存提醒。

### 5.6 `SummarizeTodayAsync`

```csharp
var text = await api.SummarizeTodayAsync(
    new List<string> { "语文", "数学", "英语", "物理" },
    cancellationToken);
```

传入空列表时返回无课文本。方法可能在 AI 不可用时返回规则总结。

### 5.7 `GenerateLearningHintAsync`

```csharp
var text = await api.GenerateLearningHintAsync(
    new List<string> { "正在上课" },
    focusSubject: "数学",
    cancellationToken);
```

当前实现会把 `subjects` 使用顿号连接后作为提示词中的“当前状态”，把 `focusSubject` 作为“学习重点”。因此若要得到明确结果，建议在 `subjects` 中传入场景文本，例如 `正在上课`、`课间，下一节课前`，而不是完整的当天课程列表。这个参数行为为兼容现有接口而保留。

### 5.8 `EstimateHomeworkLoadAsync`

```csharp
var text = await api.EstimateHomeworkLoadAsync(
    new List<string> { "数学", "语文", "英语" },
    cancellationToken);
```

当天日期会进入请求和缓存键。传入空列表时直接返回无课、无作业文本；AI 请求失败时可能返回本地规则估算。

### 5.9 触发贴心提醒

```csharp
var briefing = await api.TriggerBeforeSchoolReminderAsync(cancellationToken);
var breakText = await api.TriggerBreakReminderAsync(cancellationToken);
var summary = await api.TriggerAfterSchoolSummaryAsync(cancellationToken);
```

| 方法 | 实际行为 |
|---|---|
| `TriggerBeforeSchoolReminderAsync` | 立即生成并显示“智能每日简报”；方法名保留用于兼容，不要求当前处于任何特定时段 |
| `TriggerBreakReminderAsync` | 立即根据当前课表上下文生成并显示课间提醒 |
| `TriggerAfterSchoolSummaryAsync` | 立即生成并显示放学总结；无课时显示无课通知 |

三种手动触发默认绕过 AI 缓存。提醒提供方尚未初始化时返回 `AIIsland 贴心提醒提供方尚未就绪`。

## 6. 数据类型

### 6.1 `AIIslandChatOptions`

| 属性 | 类型 | 默认值 | 当前行为 |
|---|---|---|---|
| `Temperature` | `double?` | `null` | 非空时覆盖本次调用温度；空值使用全局设置 |
| `EnableDeepThinking` | `bool?` | `null` | 非空时覆盖本次调用的深度思考开关；空值使用设置页的全局开关 |
| `ThinkingEffort` | `ThinkingEffort?` | `null` | 非空时覆盖本次调用的思考强度（`Auto` / `Low` / `Medium` / `High`）；仅在开启深度思考时生效 |
| `MaxTokens` | `int?` | `null` | 为兼容预留；当前 `ChatAsync` 实现尚未应用该值，实际使用全局 `MaxTokens` |
| `Description` | `string?` | `null` | 显示在 `ChatAsync` 的授权确认框中 |
| `BypassCache` | `bool` | `false` | 为 `true` 时先清空 AIIsland 全局 AI 缓存，再执行请求 |

注意：`BypassCache=true` 清除的是 AIIsland 服务的全部缓存，不只清除当前调用方或当前提示词的缓存。

`ThinkingEffort` 定义在 `ClassIsland.AISmartClass.Models` 命名空间：

| 取值 | 含义 |
|---|---|
| `Auto` | 不下发强度参数，跟随模型默认 |
| `Low` | 更快、更省 |
| `Medium` | 速度与质量平衡 |
| `High` | 更深入，适合复杂问题 |

思考参数由 AIIsland 按接口族自动翻译，调用方不需要关心厂商差异：

| 接口族 | 判定依据 | 开启时下发 | 关闭时下发 |
|---|---|---|---|
| DeepSeek | 模型名 `deepseek-*` 或地址含 `deepseek` | `thinking.type=enabled` + `reasoning_effort` | `thinking.type=disabled` |
| 百炼 / Qwen | 模型名含 `qwen` / `qvq`，或地址含 `dashscope` / `aliyuncs` | `qwen3` 起：`enable_thinking=true` + `reasoning_effort`；老型号只下发 `reasoning_effort` | 仅 `qwen3` 起下发 `enable_thinking=false`，老型号不下发 |
| OpenAI | 模型名 `gpt-*` / `o1..o5`，或地址含 `api.openai.com` | 仅推理型号（`gpt-5+`、`o1/o3/o4/o5`）下发 `reasoning_effort` | 推理型号下发 `reasoning_effort=none`；`gpt-4o` 等不下发 |
| Gemini | 模型名含 `gemini` 或地址含 `googleapis` | `reasoning_effort` | 不下发（部分型号无法关闭思考） |
| 智谱 / Kimi / MiniMax / 豆包 | 模型名或地址命中对应厂商 | `thinking.type=enabled` + `reasoning_effort` | `thinking.type=disabled` |
| 其它兼容接口 | 以上都不命中 | `reasoning_effort` | 不下发 |

开启深度思考时，AIIsland 会按强度额外为思考过程预留输出 token（低 512 / 中 1024 / 高 2048，`Auto` 按 1024），避免正文被思考挤空。

### 6.2 `AIIslandChatResult`

| 属性 | 类型 | 说明 |
|---|---|---|
| `Content` | `string` | 成功时的文本；失败时通常为空 |
| `Reasoning` | `string` | 模型思考过程（`reasoning_content`）；未开启深度思考或厂商不返回时为空字符串 |
| `Success` | `bool` | 调用是否成功 |
| `Error` | `string?` | 失败原因 |
| `IsFallback` | `bool` | 返回内容是否被识别为 AIIsland 本地降级内容 |
| `DurationMs` | `long` | AI 调用阶段耗时；配置和授权失败通常为 `0` |
| `UsedWebSearch` | `bool` | 本次是否真的执行了联网检索（详见 5.1.1） |
| `SearchQuery` | `string?` | 本次联网实际使用的检索关键词，多个用「；」分隔；未联网时为 `null` |
| `WebSources` | `IReadOnlyList<AIIslandWebSource>?` | 命中的来源列表；未联网或没有结果时为 `null` |

### 6.3 `HomeworkParseResult`

| 属性 | 类型 | 说明 |
|---|---|---|
| `Success` | `bool` | 是否解析成功 |
| `ErrorMessage` | `string?` | 失败原因，JSON 字段名为 `error` |
| `Items` | `List<HomeworkParseItem>` | 作业条目 |
| `RawInput` | `string` | 原始输入，不参与 JSON 序列化 |
| `UsedLocalRules` | `bool` | 是否使用本地规则，不参与 JSON 序列化 |

`HomeworkParseItem`：

| 属性 | 类型 | 说明 |
|---|---|---|
| `Subject` | `string` | 科目 |
| `Content` | `string` | 作业内容 |
| `DueDate` | `string` | AI 返回的日期文本 |
| `Type` | `string` | 作业类型，默认 `书面作业` |
| `EstimatedMinutes` | `int` | 预计分钟数，默认 `30` |
| `ParsedDueDate` | `DateTime` | 只读转换值；无法解析时默认为明天 |

### 6.4 `ReminderParseResult`

| 属性 | 类型 | 说明 |
|---|---|---|
| `Success` | `bool` | 是否解析成功 |
| `Type` | `ReminderType` | 提醒类型 |
| `Date` | `string?` | 固定提醒日期，格式 `yyyy-MM-dd` |
| `Time` | `string?` | 时间，格式 `HH:mm` |
| `SubjectName` | `string?` | 科目关联提醒的科目 |
| `MinutesBefore` | `int` | 提前分钟数，默认 `3` |
| `Content` | `string` | 提醒正文 |
| `ErrorMessage` | `string?` | 失败原因 |
| `RawInput` | `string` | 原始输入 |

`ReminderType`：

| 名称 | 值 | 说明 |
|---|---:|---|
| `FixedTime` | `0` | 单次固定日期时间 |
| `SubjectLinked` | `1` | 指定科目课前 N 分钟 |
| `DailyRepeat` | `2` | 每日固定时间 |

## 7. 错误与并发

- 获取服务时优先使用 `IAppHost.TryGetService<IAIIslandApi>()`，避免 AIIsland 缺失时由 `GetService` 抛出异常。
- 调用前可用 `IsConfigured` 做快速检查，但它不代表网络、余额或模型名称有效。
- 全局超时设为 0 时不再限制等待时长，调用方应自行用 `CancellationToken` 控制等待；设置页「超时时间」可填写 0。
- 授权确认会自动切换到 Avalonia UI 线程；调用方不要同步阻塞异步方法，否则可能造成界面死锁。
- 相同提示词和参数在全局缓存有效期内可能复用结果；相同的并发请求可能合并为一次 HTTP 请求。图片请求的缓存键包含图片内容哈希。
- 图片识别能力结论由用户在设置页检测后写入；换模型或换接口后结论自动失效。`SupportsVision=false` 且 `IsVisionSupportVerified=false` 时只代表「按名称推断不支持」，实际请求仍可能成功。
- `CancellationToken` 取消会抛出 `OperationCanceledException`，应单独处理。
- 外部 API 没有速率配额隔离；所有插件共享 AIIsland 的模型账户、缓存和调用成本。

推荐调用结构：

```csharp
try
{
    var result = await api.ChatAsync(systemPrompt, userMessage, options, ct);
    if (!result.Success)
    {
        logger.LogWarning("AIIsland: {Error}", result.Error);
        return;
    }

    UseText(result.Content);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // 正常取消。
}
```

## 8. ClassIsland 自动化动作

除 C# API 外，AIIsland 注册了以下 ClassIsland 自动化动作。它们供用户在自动化编辑器中配置，不需要其他插件引用 `IAIIslandApi`。

| 动作 ID | 显示名称 | 主要设置 |
|---|---|---|
| `aiisland.generate-ai-notification` | 生成 AIIsland 贴心提醒 | 场景、自定义提示词、课表上下文、绕过缓存、通知标题 |
| `aiisland.refresh-components` | 刷新 AIIsland 组件 | 课表总结、学习提示、作业量估算或全部 |
| `aiisland.trigger-reminder` | 触发 AIIsland 贴心提醒 | 智能每日简报、课间提醒、放学总结、绕过缓存 |
| `aiisland.set-exam-mode` | 设置考试模式 | 启动/停止、是否打开仪表盘 |

“生成 AIIsland 贴心提醒”支持以下场景：当前学习提示、智能每日简报、课间贴心提醒、今日课表总结、放学贴心总结、作业量估算和自定义指令。

自定义指令可使用占位符：

| 占位符 | 内容 |
|---|---|
| `{currentSubject}` | 当前科目，无明确课程时为 `无` |
| `{nextSubject}` | 下一科目，无明确课程时为 `无` |
| `{todaySubjects}` | 当天科目，使用顿号连接 |
| `{timeState}` | 当前课表场景 |
| `{currentTime}` | 当前本地时间，格式 `yyyy-MM-dd HH:mm:ss` |

## 9. 兼容性说明

- 公开契约位于 `ClassIsland.AISmartClass.PublicApi`；解析结果模型位于 `ClassIsland.AISmartClass.Models`。
- 当前没有独立的 API-only 程序集或 NuGet 包，调用方与 AIIsland DLL 存在编译时版本耦合。
- `TriggerBeforeSchoolReminderAsync` 名称暂时保留，但内容已经是“智能每日简报”。
- `GenerateLearningHintAsync` 的第一个参数名仍为 `subjects`，实际被当作场景文本使用，详见 5.5。
- `AIIslandChatOptions.MaxTokens` 当前为兼容预留字段，尚未覆盖单次调用参数。
- 联网搜索需要设置页开启并配置 OpenSERP 实例地址。未配置时 `EnableWebSearch = true` 会静默退化为普通对话，不会报错。

升级 AIIsland 后，建议至少验证服务解析、一次授权确认、一次 `ChatAsync` 和调用取消流程。
