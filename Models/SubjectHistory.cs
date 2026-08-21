namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 科目历史表现记录（扩展用）。
/// 可持久化到本地 JSON，用于长期学习建议。
/// </summary>
public class SubjectHistory
{
    /// <summary>科目名称</summary>
    public string SubjectName { get; set; } = "";

    /// <summary>日期</summary>
    public DateTime Date { get; set; } = DateTime.Today;

    /// <summary>用户自评难度 1-5（可选）</summary>
    public int? SelfRatedDifficulty { get; set; }

    /// <summary>AI 预估难度 1-5</summary>
    public int AiEstimatedDifficulty { get; set; } = 3;

    /// <summary>备注</summary>
    public string? Note { get; set; }
}
