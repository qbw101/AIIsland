# 匿名遥测：在线状态 / 错误 / 崩溃

AIIsland 内置的遥测模块，用于回答三个问题：

1. **现在有多少人在用？** —— TCP 长连接 + 30 秒心跳，服务端按连接判定在线。
2. **用户遇到了什么错误？** —— `Logger.Error` 全量旁路 + 业务代码显式上报，服务端按指纹聚合。
3. **是不是插件把 ClassIsland 搞崩了？** —— 未处理异常即时上报；进程被直接干掉时，下次启动补报现场（日志尾 + Windows 事件日志 + 转储信息）。

传输方式：**插件 → 服务器走裸 TCP（NDJSON 长连接）**，浏览器 → 服务器走 HTTP。服务器代码见独立仓库目录 `telemetry-server/`。

---

## 1. 代码位置

| 文件 | 职责 |
|---|---|
| `Models/TelemetrySettings.cs` | 配置项（开关 / 是否附带日志）与内置接入点常量（域名 / 端口 / 令牌） |
| `Services/Telemetry/TelemetryProtocol.cs` | 帧与负载定义、序列化选项 |
| `Services/Telemetry/TelemetryClient.cs` | TCP 连接、握手、断线重连、发送队列、bye |
| `Services/Telemetry/TelemetryService.cs` | 生命周期、心跳、错误/崩溃上报、标记文件、崩溃 spool |
| `Services/Telemetry/CrashForensics.cs` | 日志尾 / Windows 事件日志 / CrashDumps 取证 |
| `Services/Logger.cs` | 新增 `ErrorReported` 事件，把已有错误日志接到遥测 |
| `Plugin.cs` | 创建服务、启动/关闭、设置同步 |
| `Views/SettingsPages/AISettingsPage.axaml(.cs)` | 「更多选项 → 诊断与遥测」二级设置分组 |

```
ClassIsland
  └─ Plugin.Initialize → new TelemetryService(settings, PluginConfigFolder, persist)
        └─ PluginInitializer.StartAsync → telemetry.Start()
              ├─ TelemetryClient（TCP，自动重连，队列上限 512，满了丢最旧）
              ├─ 心跳循环（30s，同时刷新本地标记文件）
              ├─ Logger.ErrorReported → 错误上报（同问题 5 分钟去重）
              ├─ AppDomain / Dispatcher / TaskScheduler 异常钩子 → 崩溃上报
              └─ 启动时检查上次会话标记 → 未正常退出则补报崩溃现场
```

## 2. 上报什么、不上报什么

| 上报 | 不上报 |
|---|---|
| 匿名安装 ID（首次启动随机生成）、会话 ID | 用户名、QQ、邮箱、机器码、硬件序列号 |
| 插件版本、ClassIsland 版本、Windows 版本、系统语言 | 课表内容、课程名、同学姓名、值日生名单 |
| AI 接口域名（如 `api.deepseek.com`，不含路径与 Key） | API Key、完整接口地址、请求体 |
| 异常类型、消息、堆栈、发生场景、自定义上下文 | 提示词、AI 回复、聊天记录、AI 调用日志全文 |
| 崩溃时的 ClassIsland 日志尾部（可在设置里关闭） | 本地文件内容、剪贴板、截图 |

- 日志尾部**可能**包含课表名称等本地文本，因此设置页提供「崩溃时附带日志」开关，默认开启、可随时关闭。
- 所有上报内容都会经过一次密钥清洗（`sk-*`、`apiKey=…`、`Bearer …` 会被替换成 `***`）。
- IP 只在内存里用于排查，落库时打码成 `1.2.3.x`，不存完整地址。

## 3. 配置

设置页 → **更多选项** → **诊断与遥测**（二级折叠菜单）：

| 项 | 说明 |
|---|---|
| 匿名遥测 | 总开关，关闭后不建立任何连接 |
| 崩溃时附带日志 | 关闭后崩溃只上报异常本身 |

服务器地址、端口与接入令牌不再暴露给用户，写死在 `Models/TelemetrySettings.cs` 的常量里：

| 常量 | 值 |
|---|---|
| `DefaultServerHost` | `cn-sh-5.qwq.fan` |
| `DefaultServerPort` | `49058` |
| `DefaultToken` | `tlm_14qXkdcbCB8dr5CPSx6ztiZQcsqEE3s1` |

对应 `aisettings.json`：

```json
{
  "telemetry": {
    "enabled": true,
    "serverHost": "cn-sh-5.qwq.fan",
    "serverPort": 49058,
    "token": "tlm_14qXkdcbCB8dr5CPSx6ztiZQcsqEE3s1",
    "includeLogTail": true,
    "installId": "9c5a7e32…",
    "reportCrashes": true
  }
}
```

> **换接入点**：只改 `TelemetrySettings` 里的三个常量。旧配置若仍是早期默认的 `telemetry.qbwnas.top:9700` 或地址为空，`MigrateAISettings` 会在启动时自动改写成内置接入点；令牌为空也会补齐。
>
> 令牌随源码公开，任何人拿到都能伪造上报。它是「接入凭证」而非「读取凭证」，仪表盘与数据库权限另由服务器控制。

## 4. 三条上报链路

### 4.1 在线

- 连接建立后发 `hello`（携带版本、系统、语言、是否首次运行）。
- 每 30 秒发 `heartbeat`，服务器刷新 `last_seen`。
- 退出时 `PluginInitializer.StopAsync` 发 `bye`，服务器标记 `graceful`。
- 服务器判定：连接存活，或断开未超过 75 秒宽限。

### 4.2 错误

已有代码里所有 `Logger.Error(...)` 会自动进入遥测，不需要逐个改：

```csharp
Logger.Error($"保存设置失败: {ex.Message}");   // 自动上报
Logger.Error(ex, "AI 调用失败");               // 自动上报（带堆栈）
```

业务代码想补充上下文时：

```csharp
try
{
    await _aiService.SendAsync(prompt);
}
catch (Exception ex)
{
    TelemetryService.Current?.ReportError(ex, "AI 调用", new Dictionary<string, string>
    {
        ["model"] = _settings.Model,
        ["elapsedMs"] = sw.ElapsedMilliseconds.ToString()
    });
    throw;
}
```

去重规则：同一「场景 + 类型 + 消息」5 分钟内只上报一次，避免日志风暴打满队列。

### 4.3 崩溃

| 场景 | 机制 |
|---|---|
| 托管未处理异常 | `AppDomain.CurrentDomain.UnhandledException` → 立即 `crash` 帧，同步等待最多 4 秒 |
| Avalonia UI 线程异常 | `Dispatcher.UIThread.UnhandledException` → 同上（不修改 `Handled`，不改变宿主行为） |
| 进程被直接终止 / 原生崩溃 | 无法当场上报。启动时发现上次 `session.json` 的 `cleanExit=false` → 补报 `crash`，附日志尾、Windows 事件日志、`%LOCALAPPDATA%\CrashDumps` 里的转储信息 |

崩溃帧会先落盘到 `Data/telemetry/crash-*.json`，发送成功后再删除；发送失败则留到下次启动补传（最多 20 条）。

## 5. 本地文件

| 路径 | 内容 |
|---|---|
| `Data/telemetry/session.json` | 当前/上次会话标记（sessionId、开始时间、最后心跳、cleanExit） |
| `Data/telemetry/crash-*.json` | 待补传的崩溃负载 |

删掉这两个文件不会影响插件，只会丢失一次崩溃补报。

## 6. 自测

1. 启动服务器：`cd telemetry-server && dotnet run`。
2. 本机联调时直接改 `aisettings.json` 的 `serverHost` / `serverPort` 指向 `127.0.0.1` / `9700`（设置页已不提供这两个输入框），保存后重启 ClassIsland。
3. 插件日志出现「已连接遥测服务器」，仪表盘「在线」页应出现本机会话。
4. 验证崩溃补报：在任务管理器结束 ClassIsland 进程 → 重新启动 ClassIsland → 仪表盘出现「进程异常退出」。

命令行联调（不需要 ClassIsland）：

```bash
cd telemetry-smoke
dotnet run              # 正常连接 → 测试事件 + 一条错误 → 正常退出
dotnet run -- --crash   # 直接杀进程；再运行一次 dotnet run 验证崩溃补报
```

## 7. 行为边界

- 所有网络操作在后台线程，队列非阻塞：网络断了不会卡住 ClassIsland 主线程。
- 重连退避 3s → 6s → 12s → 24s → 48s → 60s；令牌错误时停止重试，等待下一版内置接入点更新。
- 上报失败不影响任何业务逻辑，`Logger` 的旁路回调包在 try/catch 中。
- 服务器不可达时插件只保留一个后台重连任务，无日志刷屏（只在状态变化时记录一行）。
