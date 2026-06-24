namespace LeadStoreAutoBot.Models;

/// <summary>Проект на стороне CRM (вернёт API в команде gck_projects).</summary>
public class ProjectInfo
{
    public string Name { get; set; } = "";
    public string Src { get; set; } = "";
    public string Content { get; set; } = "";
    public int Status { get; set; }
}
