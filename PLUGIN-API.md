# AIIsland 插件 API 文档

> 对应 AIIsland `1.4.0.0`，ClassIsland API `2.0.0.0`  
> 依据当前 `IAIIslandApi`、`AIIslandApi` 和授权实现整理，更新于 2026-08-08

## 1. API 范围

AIIsland 向其他 ClassIsland 插件注册 `IAIIslandApi` 单例。调用方可以复用用户在 AIIsland 中配置的模型、API 地址、密钥、缓存、重试和提示词，无需再次保存密钥。

当前公开能力包括：

- 通用 AI 对话
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

`IsConfigured` 和 `ModelName` 不需要授权，其余公开方法均经过授权检查。

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

    Task<AIIslandChatResult> ChatAsync(
        string systemPrompt,
        string userMessage,
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

### 4.2 返回行为总览

| 方法 | 成功返回 | 未配置或拒绝授权 | AI/网络失败 |
|---|---|---|---|
| `ChatAsync` | `AIIslandChatResult.Success=true` | `Success=false` | `Success=false`，错误写入 `Error` |
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

### 5.2 `ParseHomeworkAsync`

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

### 5.3 `ParseReminderAsync`

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

### 5.4 `SummarizeTodayAsync`

```csharp
var text = await api.SummarizeTodayAsync(
    new List<string> { "语文", "数学", "英语", "物理" },
    cancellationToken);
```

传入空列表时返回无课文本。方法可能在 AI 不可用时返回规则总结。

### 5.5 `GenerateLearningHintAsync`

```csharp
var text = await api.GenerateLearningHintAsync(
    new List<string> { "正在上课" },
    focusSubject: "数学",
    cancellationToken);
```

当前实现会把 `subjects` 使用顿号连接后作为提示词中的“当前状态”，把 `focusSubject` 作为“学习重点”。因此若要得到明确结果，建议在 `subjects` 中传入场景文本，例如 `正在上课`、`课间，下一节课前`，而不是完整的当天课程列表。这个参数行为为兼容现有接口而保留。

### 5.6 `EstimateHomeworkLoadAsync`

```csharp
var text = await api.EstimateHomeworkLoadAsync(
    new List<string> { "数学", "语文", "英语" },
    cancellationToken);
```

当天日期会进入请求和缓存键。传入空列表时直接返回无课、无作业文本；AI 请求失败时可能返回本地规则估算。

### 5.7 触发贴心提醒

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
| `MaxTokens` | `int?` | `null` | 为兼容预留；当前 `ChatAsync` 实现尚未应用该值，实际使用全局 `MaxTokens` |
| `Description` | `string?` | `null` | 显示在 `ChatAsync` 的授权确认框中 |
| `BypassCache` | `bool` | `false` | 为 `true` 时先清空 AIIsland 全局 AI 缓存，再执行请求 |

注意：`BypassCache=true` 清除的是 AIIsland 服务的全部缓存，不只清除当前调用方或当前提示词的缓存。

### 6.2 `AIIslandChatResult`

| 属性 | 类型 | 说明 |
|---|---|---|
| `Content` | `string` | 成功时的文本；失败时通常为空 |
| `Success` | `bool` | 调用是否成功 |
| `Error` | `string?` | 失败原因 |
| `IsFallback` | `bool` | 返回内容是否被识别为 AIIsland 本地降级内容 |
| `DurationMs` | `long` | AI 调用阶段耗时；配置和授权失败通常为 `0` |

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
- 授权确认会自动切换到 Avalonia UI 线程；调用方不要同步阻塞异步方法，否则可能造成界面死锁。
- 相同提示词和参数在全局缓存有效期内可能复用结果；相同的并发请求可能合并为一次 HTTP 请求。
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

升级 AIIsland 后，建议至少验证服务解析、一次授权确认、一次 `ChatAsync` 和调用取消流程。
