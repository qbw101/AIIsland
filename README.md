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
![ganyu2.png](http://image.qbwnas.top/openlist/d/图床/ganyu2.png?sign=6K5ByOZ9e5fGcpCQTCqvuI3FP8XZvMNG7-MJnDbaVss=:0)
### 提醒
| 功能 | 触发时机 | 说明 |
|---|---|---|
| 课前提醒 | 课间开始时 | AI 根据刚上完的课 + 下节课科目生成个性化全屏提醒 |
| 放学总结 | 放学时 | AI 生成本日学习总结全屏遮罩，含复习建议 |
| 换课提醒 | 检测到临时换课时 | 自动弹出提示告知课表变动 |
| 语音播报 | 随提醒触发	 | 可选开启，默认关闭以免影响课堂 |
| 自定义提醒 | 支持三种：固定时间（一次性）、每日重复、关联科目课前 N 分钟 | 自由设置提醒内容和触发条件，可随时开关/编辑/删除 |

支持 AI 离线降级：API 不可用时自动回退到本地预设句子库

换课提醒无 AI 调用，直接弹提示
### 组件
| 组件 |	显示名 | 功能 |
|----|----|----|
|ScheduleInsight |	AIIsland 课表总结 |	AI 生成一句话解读今日课表|
|HomeworkEstimate | AIIsland 作业量估算 | 根据科目类型估算今日作业量（AI + 规则兜底）|
|ClassCountdown | 课时倒计时 | 当前课时剩余时间 + 进度条，实时刷新|
|CurrentHint | AIIsland 课程提示 | 每次上课自动生成当前课程学习提示，换课自动更新|
|DifficultyInfo | 难度与番茄钟 | 今日课程难度星数+ 专注时长建议|

### 其他
* 考试模式
* 欢迎向导


### 功能展示
* 课前提醒
![课前提醒.png](http://image.qbwnas.top/openlist/d/图床/课前提醒.png?sign=iGUOB4bTmpDoA5S5vaItNyk7PYafPzVz83LSlh5oQ1w=:0)
* 放学总结
![放学总结.png](http://image.qbwnas.top/openlist/d/图床/放学总结.png?sign=_LCgKfxKbB44QeUyjfe-cv5wkuYQ5LxfgoRvr3KcY34=:0)
* 换课提醒
![换课提醒.png](http://image.qbwnas.top/openlist/d/图床/换课提醒.png?sign=u6ksJ-zHjJRqZMTPpWcsaxmNH-7flD2LDuTMMKPswGA=:0)
* 组件
![组件.png](http://image.qbwnas.top/openlist/d/图床/组件.png?sign=QfY-e4BB0cqPdMrByBM_3ZPu66DY0q7ujYIdVjeuWy8=:0)
* 自定义提醒
![自定义提醒.png](http://image.qbwnas.top/openlist/d/图床/自定义提醒.png?sign=6OB-nYMiakO2ujRDQZvC45DSYmpgtqpe4ImyBHBsoJ4=:0)
* 考试模式
![考试模式.png](http://image.qbwnas.top/openlist/d/图床/考试模式.png?sign=DDVZ-hzaNeO6sGd0NXH-2W3arBOzPBSl9oAA3aQKcL0=:0)
* 欢迎向导
![欢迎向导.png](http://image.qbwnas.top/openlist/d/图床/欢迎向导.png?sign=HqZAe9cEQYRGWjBv8znE1m32SHMAz4kRQGJR7SQ8t7U=:0)


## 一些未来的计划 ~~画饼~~  
![ganyu1.png](http://image.qbwnas.top/openlist/d/图床/ganyu1.png?sign=XT0Dolm9YWKTcRjMosh337Nr5JqozRG27SbPMpxE2-4=:0)
* 语音播报优化：
    * 目前此功能尚不稳定，尽量不要使用    
    * 在未来我们将适配花儿不哭大佬的GPT-SoVITS在线推理api
* 体验与功能打磨
* 接入自动化


本项目使用了鸿蒙系统内的图标，非常感谢！！！

本插件在 ClassIsland 插件 SDK 的开源许可（**LGPLv3**）下分发。

> ⚠️ **本插件的全部代码均由 AI（大语言模型）编写生成。**
> 作者负责需求设计、调试验证与发布，具体实现代码由 AI 辅助完成。使用前请自行评估代码质量与安全性。
