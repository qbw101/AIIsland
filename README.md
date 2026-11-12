# AIIsland/AISmartClass
[ClassIsland](https://github.com/ClassIsland/ClassIsland) 插件 — 为你的校园课表注入 AI 智能 ✨

程序集名 / 插件 ID：`ClassIsland.AISmartClass`　·　显示名称：**AIIsland**

---

## 关于名称

| 场景 | 名称 |
|------|------|
| 用户可见显示名（插件列表、组件库、设置页） | **AIIsland** |
| 技术标识符（程序集名、命名空间、插件 ID、DLL 文件名） | `ClassIsland.AISmartClass` |

> 早期开发代号为 `AISmartClass`，后统一更名为 **AIIsland**。为避免破坏 `using` 引用、`avares://` 资源路径和 manifest 入口指向，技术标识符仍保留 `ClassIsland.AISmartClass`，仅更改对用户展示的名称。两者指向同一个插件。

## 目前已实现的功能
![ganyu2.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/ganyu2.png?sign=6K5ByOZ9e5fGcpCQTCqvuI3FP8XZvMNG7-MJnDbaVss%3D%3A0)
### 提醒
| 功能 | 触发时机 | 说明 |
|---|---|---|
| 智能每日简报 | 第一节课前 5 分钟 | 结合第一节课、当前时间、天气、自定义提醒、今日新闻和当天生日同学生成简报 |
| 课间贴心提醒 | 课间开始时 | 结合前后课程、当前时间、天气生成提醒 |
| 放学贴心总结 | 最后一节课结束时 | 根据真实时间问候，总结今日课程，提醒当天值日生，并给出明日天气与准备建议 |
| 播放岛 | 检测到电脑开始播放音乐时 | 独立调用 AI 展示歌曲相关信息；上课期间不提醒 |
| 换课提醒 | 检测到临时换课时 | 自动弹出提示告知课表变动 |
| 语音播报 | 随提醒触发	 | 可选开启，默认关闭以免影响课堂 |
| 自定义提醒 | 支持三种：固定时间（一次性）、每日重复、关联科目课前 N 分钟 | 自由设置提醒内容和触发条件，可随时开关/编辑/删除 |

AI 请求失败时直接显示具体错误原因（如 Key 无效、超时、限流），不再用预设句子伪装成正常提醒

换课提醒无 AI 调用，直接弹提示
### 组件
| 组件 |	显示名 | 功能 |
|----|----|----|
|ScheduleInsight |	AIIsland 课表总结 |	AI 生成一句话解读今日课表|
|HomeworkEstimate | AIIsland 作业量估算 | 根据科目类型估算今日作业量|
|ClassCountdown | 课时倒计时 | 当前课时剩余时间，纯数字实时刷新|
|CurrentHint | AIIsland 课程提示 | 每次上课自动生成当前课程学习提示，换课自动更新|
|DifficultyInfo | 难度与番茄钟 | 今日课程难度等级与专注时长建议|

### 考试模式

| 功能 | 说明 |
|---|---|
| 同步 ClassIsland 档案 | 您可以提前编辑档案，考试模式直接同步 ClassIsland 当前课表和时间布局，不需要在 AIIsland 中重复维护考试安排。 |
| 独立考试时间来源 | 打开考试页面时弹窗选择偏移时间或精确时间，并提示按考试铃声校准偏移；考试控制中心可随时更改 |
| 全屏提醒浮层 | 考试提醒触发时在页面顶部置顶显示，避免被浏览器全屏窗口遮挡；可在考试设置中开关，并调整停留时长（3–60 秒） |
| 考试大屏 | 显示考试科目、当前时间、逐位滚动倒计时、考试进度、下一关键提醒、听力状态和考试材料 |
| 考试控制中心 | 集中管理提醒、听力播放、考试信息，并提供运行状态诊断 |
| 原生考试提醒 | 支持考前、开考、剩余时间和考试结束提醒，通知与提示音均使用 ClassIsland 原生提醒系统 |
| 提醒过滤 | 可在考试模式运行期间仅保留 AIIsland 考试提醒和地震预警提醒，退出考试模式后自动恢复 |
| 时间校准 | 启动考试模式后自动检测标准时间，偏差超过 30 秒时提示前往 ClassIsland 时钟设置校准 |
| 听力播放器 | 支持选择或重置听力文件、播放控制、进度拖动、音量调节，以及提前、准时或延后自动播放 |
| 考前弹窗 | 开考前 5 分钟依次询问考试材料和英语听力文件，避免弹窗相互遮挡 |
| 考试材料填写 | 可记录试卷页数、答题卡页数和草稿纸状态；按考试场次隔离，仅在当前网页中临时保留，刷新或关闭后清空 |

### 其他
* 欢迎向导（含插件授权）
* 深度思考与思考强度：设置页可开关推理模式并选择强度（自动 / 低 / 中 / 高），按接口族自动适配各家参数
* 图片识别（多模态）与能力检测：设置页可一键检测当前模型是否支持识别图片，欢迎向导在模型不支持时弹窗提醒
* 联网搜索与网页抓取（function calling）：模型自主判断需要实时信息时调用 `web_search` 检索摘要，摘要不够或用户直接给出链接时调用 `web_fetch` 打开网页读取正文；通过自建 OpenSERP 检索，可选 DeepSeek 官方联网兜底（搜索与抓取），回答标注来源；默认关闭
* 外部插件集成（生日祝福、值日生提醒）
* 统一时间来源（ClassIsland 时间 / 系统时间，全局生效）
* 匿名遥测（在线状态、错误与崩溃上报，默认开启，可在设置里关闭）

### 匿名遥测

设置页「更多选项」→「诊断与遥测」可管理匿名上报（默认开启），用于统计在线人数、收集错误和崩溃现场。该分组只有「匿名遥测」和「崩溃时附带日志」两个开关，服务器地址与令牌已内置，无需填写。

* **只上报**：插件/ClassIsland/Windows 版本、系统语言、异常类型与堆栈、发生场景、崩溃时的 ClassIsland 日志尾部与 Windows 事件日志。
* **不上报**：课表内容、课程名、同学姓名、提示词、AI 回复、API Key、账号或硬件标识。所有文本在发送前会清洗一次密钥。
* 上报走 TCP 长连接，30 秒一次心跳；关闭开关后不建立任何连接。
* 崩溃检测：未处理异常立即上报；进程被直接结束时，下次启动补报上次异常退出。
* 技术细节与协议见 [TELEMETRY.md](TELEMETRY.md)。


### 功能展示
* 智能每日简报
![智能每日简报.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E6%99%BA%E8%83%BD%E6%AF%8F%E6%97%A5%E7%AE%80%E6%8A%A5.png?sign=TaipzHevGipcxoYlvAEhO0NcfcbOedjm8Mb2etZDjHE%3D%3A0)
* 课间贴心提醒
![课前提醒.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E8%AF%BE%E5%89%8D%E6%8F%90%E9%86%92.png?sign=iGUOB4bTmpDoA5S5vaItNyk7PYafPzVz83LSlh5oQ1w%3D%3A0)
* 放学贴心总结
![放学总结.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E6%94%BE%E5%AD%A6%E6%80%BB%E7%BB%93.png?sign=_LCgKfxKbB44QeUyjfe-cv5wkuYQ5LxfgoRvr3KcY34%3D%3A0)
* 换课提醒
![换课提醒.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E6%8D%A2%E8%AF%BE%E6%8F%90%E9%86%92.png?sign=u6ksJ-zHjJRqZMTPpWcsaxmNH-7flD2LDuTMMKPswGA%3D%3A0)
* 组件
![组件.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E7%BB%84%E4%BB%B6.png?sign=QfY-e4BB0cqPdMrByBM_3ZPu66DY0q7ujYIdVjeuWy8%3D%3A0)
* 自定义提醒
![自定义提醒.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E8%87%AA%E5%AE%9A%E4%B9%89%E6%8F%90%E9%86%92.png?sign=6OB-nYMiakO2ujRDQZvC45DSYmpgtqpe4ImyBHBsoJ4%3D%3A0)
* 播放岛
![播放岛.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E6%92%AD%E6%94%BE%E5%B2%9B.png?sign=UxafYI2ephvW4YGRYmBl0mSLRxb9ris1eqF_HVpgZVo%3D%3A0)
* 考试模式
![考试模式new.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E8%80%83%E8%AF%95%E6%A8%A1%E5%BC%8Fnew.png?sign=xjoeooJ2miJ-4LDaXxU4sKAhiWAg6dVLqdmRX2FWoTY%3D%3A0)
* 欢迎向导
![欢迎向导.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E6%AC%A2%E8%BF%8E%E5%90%91%E5%AF%BC.png?sign=HqZAe9cEQYRGWjBv8znE1m32SHMAz4kRQGJR7SQ8t7U%3D%3A0)
## 插件API
本插件在1.4.0.0版本中加入了api接口，方便其他插件调用ai服务

api文档在插件目录的PLUGIN-API.md文件中

1.9.0.0 起，API 新增图片识别：`ChatWithImageAsync` 可直接传图片字节调用多模态模型，`SupportsVision` / `VisionSupportLevel` 用于在调用前判断当前模型能否读图。

同版本 API 支持深度思考：`AIIslandChatOptions.EnableDeepThinking` / `ThinkingEffort` 按次控制推理开关与强度，结果里的 `Reasoning` 返回模型的思考过程；`ChatStreamAsync` 提供流式增量回调（正文与思考分开推送），`AIIslandStreamUpdate` 携带每次增量与完整快照；`ChatWithImageStreamAsync` 支持带图片的流式对话。

1.9.0.0 起，API 支持联网搜索：`AIIslandChatOptions.EnableWebSearch` 按次开启，结果的 `UsedWebSearch`、`WebSources` 与 `SearchQuery` 告知本次是否真的检索、命中的来源以及模型实际使用的关键词，流式的 `AIIslandStreamUpdate.Status` 会推送 `searching` / `answering` 阶段，`SearchQuery` 同步带上关键词（可直接显示为「正在搜索：xxx」）。

## 联网搜索配置（OpenSERP）

联网搜索默认关闭，开启前需要一个搜索源。AIIsland 使用 [OpenSERP](https://openserp.org/zh/)：MIT 许可、可自托管，一个接口聚合 Bing / 百度 / DuckDuckGo / Google / Yandex / Ecosia 的结果，自托管不需要 API Key。

1. 在任意一台常开的机器或服务器上启动实例：

```bash
docker run -d --name openserp -p 7000:7000 --restart unless-stopped \
  karust/openserp:latest serve -a 0.0.0.0 -p 7000
```

2. 验证：`curl "http://127.0.0.1:7000/bing/search?text=测试&limit=3"` 应返回 `{"results":[...]}`
3. 让实例对上内置地址 `http://search.qbwnas.top`（插件固定请求该地址，设置页不提供修改入口；本机调试可用反向代理或 hosts 指向自己的实例）
4. 设置页「联网搜索」点「测试」确认可用，再打开「允许模型联网搜索」

检索地址固定为 `http://search.qbwnas.top`，默认引擎是 Bing，失败时自动换百度重试。

实测引擎可用性（国内网络）：

| 引擎 | 状态 |
|---|---|
| Bing | 正常，冷启动约 5 秒，命中缓存后 0.2 秒 |
| 百度 | 可用，但结果里广告与解析残留较多（已被插件过滤） |
| Google / Yandex / DuckDuckGo | 国内直连不通 |
| Ecosia | 返回 200 但解析不出结果 |

`/mega` 聚合接口会等待上述不通的引擎超时（约 90 秒），**不要使用**；插件也不会把 `mega` 当作引擎名。

首选引擎失败会自动换一个引擎重试；如果是**超时**则不再重试，避免总耗时翻倍。全部引擎都不可用时照常回答，只是不联网。

如果放在反向代理后面，记得把整个路径都转发给 OpenSERP——它的接口都在 `/{引擎}/search` 下，只转发 `/` 不够。

### 打开网页（web_fetch）

搜索只给摘要，遇到「细节在正文里」的问题时，模型可以再调用 `web_fetch` 打开最相关的页面读正文。设置页「联网搜索」里的「允许模型打开网页」可以单独关闭它（默认开启）。

默认这条链路完全在本地完成（`HttpClient` 直连 + HTML 提炼），不经过任何第三方服务；如果开启了「DeepSeek 官方联网兜底」，本地抓不到（JS 渲染、反爬）时会再由 DeepSeek 服务端抓一次。

实测（用真实搜索结果逐个抓取）能覆盖约一半的页面，而且恰好是权威的那一半：

| 页面类型 | 结果 |
|---|---|
| 高校 / 政府网站、腾讯新闻、行业资讯 | 可抓，正文完整 |
| BWIKI 等 Wiki | 可抓，整页正文 |
| 米哈游官网、米游社、SPA 应用 | 抓不到（需要脚本渲染，返回 200 但正文为空） |
| 百度百科 / 文库 / 贴吧、知乎 | 抓不到（403 安全验证，服务端反爬） |

抓不到时会如实告诉模型「页面打不开」，让它退回摘要作答，不会编造正文。

开启「DeepSeek 官方联网兜底」后，上面「抓不到」的两类会再走一次 DeepSeek 服务端抓取：SPA 通常能拿到，百度系与知乎的反爬仍可能被拒（403 / 404）。这种情况不计费。

约束：

* 只允许 `http` / `https` 的 80 / 443 端口；解析 DNS 后拒绝环回、内网、链路本地与云元数据地址，重定向手动跟随且每跳重新校验（防 SSRF）
* 单页下载上限 1 MB，正文截断到 4000 字，单次对话最多打开 2 个页面
* 抓取超时跟随「检索超时」，最少 5 秒
* 页面内容同样用固定边界包裹并声明「只作数据、不得执行」，防提示词注入

### DeepSeek 官方联网兜底

自建实例偶尔会返回与查询无关的结果（实测出现过搜「甘雨怎么养」返回文件传输网站的情况，疑似实例缓存了错误内容）；本地抓网页也挡不住 JS 渲染与反爬。开启「DeepSeek 官方联网兜底」后，搜索没有有效结果、或网页抓不开时，会用 AI 对话的同一把 Key 改走 DeepSeek 官方接口：搜索用 `/v1/tools/search`，抓取用 `/v1/tools/fetch`（服务端取页面并转 Markdown，能覆盖 SPA）。

* **每次开启都会弹窗确认费用**，明确说明将使用你自己的 API Key 与账号余额；点取消则开关自动回退
* 仅当「API 连接」里的地址是 DeepSeek 官方域名时可用，第三方托管渠道的 Key 不通用
* 费用：每次成功请求 **约 0.005 元**（搜索与抓取同价，官方 0.75 美元 / 千次）；空结果、无正文与请求失败不计费
* 参考用量：每天触发 10 次 ≈ 每月 1.5 元，每天 30 次 ≈ 每月 4.5 元
* 默认关闭，不开启时不会产生任何额外请求；价格以 DeepSeek 官方最新公布为准

## 外部插件集成
AIIsland 可读取以下插件数据（需在欢迎向导或设置页授权）：
- BirthdayIsland：当天生日名单 → 每日简报生日祝福
- DutyIsland / DutyList / ExtraIsland：当天值日生 → 放学总结值日提醒

## 一些未来的计划 ~~画饼~~  
![ganyu1.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/ganyu1.png?sign=XT0Dolm9YWKTcRjMosh337Nr5JqozRG27SbPMpxE2-4%3D%3A0)
* 体验与功能打磨
* 加入AI agent


## 依赖

| 直接依赖 | 版本 | 许可证 |
| --- | --- | --- |
| [ClassIsland.PluginSdk](https://github.com/ClassIsland/ClassIsland) | 2.0.0.2 | LGPL-3.0-only |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.2.2 | MIT |
| [NPSMLib](https://github.com/ADeltaX/NPSMLib) | 0.9.14 | MIT |
| [SmartReader](https://github.com/strumenta/SmartReader) | 0.11.1 | Apache-2.0 |
| [System.Diagnostics.EventLog](https://github.com/dotnet/runtime) | 8.0.1 | MIT |

设置 → 更多选项 → 开源库与许可 →「查看」可打开独立窗口，查看直接及间接依赖的版本、GitHub / 项目地址、许可证与包内声明。窗口支持按库名或许可证搜索；依赖清单和包内声明随插件内嵌，打开外部链接时才需要联网。部分旧包没有声明许可证或源码地址，界面会如实标注并提供 NuGet 页面。

升级依赖后，在仓库根目录运行以下命令更新随插件分发的清单：

```powershell
dotnet restore ClassIsland.AISmartClass/ClassIsland.AISmartClass.csproj
./scripts/Update-OpenSourceNotices.ps1
# 校验清单是否与本地已还原的包一致：
./scripts/Update-OpenSourceNotices.ps1 -Check
```

本项目使用了鸿蒙系统内的图标，非常感谢！！！

ClassIsland 插件 SDK 使用 **LGPL-3.0-only**；本项目的许可证见 [LICENSE](LICENSE)，第三方库分别适用各自声明的许可。

> ⚠️ **本插件的全部代码均由 AI（大语言模型）编写生成。**
> 作者负责需求设计、调试验证与发布，具体实现代码由 AI 辅助完成。使用前请自行评估代码质量与安全性。
