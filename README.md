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

支持 AI 离线降级：API 不可用时自动回退到本地预设句子库

换课提醒无 AI 调用，直接弹提示
### 组件
| 组件 |	显示名 | 功能 |
|----|----|----|
|ScheduleInsight |	AIIsland 课表总结 |	AI 生成一句话解读今日课表|
|HomeworkEstimate | AIIsland 作业量估算 | 根据科目类型估算今日作业量|
|ClassCountdown | 课时倒计时 | 当前课时剩余时间 + 进度条，实时刷新|
|CurrentHint | AIIsland 课程提示 | 每次上课自动生成当前课程学习提示，换课自动更新|
|DifficultyInfo | 难度与番茄钟 | 今日课程难度星数+ 专注时长建议|

### 其他
* 考试模式
* 欢迎向导（含插件授权）
* 外部插件集成（生日祝福、值日生提醒）


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
![考试模式.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E8%80%83%E8%AF%95%E6%A8%A1%E5%BC%8F.png?sign=DDVZ-hzaNeO6sGd0NXH-2W3arBOzPBSl9oAA3aQKcL0%3D%3A0)
* 欢迎向导
![欢迎向导.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/%E6%AC%A2%E8%BF%8E%E5%90%91%E5%AF%BC.png?sign=HqZAe9cEQYRGWjBv8znE1m32SHMAz4kRQGJR7SQ8t7U%3D%3A0)
## 插件API
本插件在1.4.0.0版本中加入了api接口，方便其他插件调用ai服务

api文档在插件目录的PLUGIN-API.md文件中

## 外部插件集成
AIIsland 可读取以下插件数据（需在欢迎向导或设置页授权）：
- BirthdayIsland：当天生日名单 → 每日简报生日祝福
- DutyIsland / DutyList / ExtraIsland：当天值日生 → 放学总结值日提醒

## 一些未来的计划 ~~画饼~~  
![ganyu1.png](http://image.qbwnas.top/openlist/d/%E5%9B%BE%E5%BA%8A/ganyu1.png?sign=XT0Dolm9YWKTcRjMosh337Nr5JqozRG27SbPMpxE2-4%3D%3A0)
* 体验与功能打磨


## 依赖

- [ClassIsland.PluginSdk](https://github.com/HelloWRC/ClassIsland) 2.0.0.2（MIT）
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.2.2（MIT）
- [NPSMLib](https://github.com/ADeltaX/NPSMLib) 0.9.14（MIT）

本项目使用了鸿蒙系统内的图标，非常感谢！！！

本插件在 ClassIsland 插件 SDK 的开源许可（**LGPLv3**）下分发。

> ⚠️ **本插件的全部代码均由 AI（大语言模型）编写生成。**
> 作者负责需求设计、调试验证与发布，具体实现代码由 AI 辅助完成。使用前请自行评估代码质量与安全性。
