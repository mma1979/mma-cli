namespace Mma.Cli.Database.Entities;

public sealed class WorkspaceEntityRecord
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string PkType { get; set; } = "Guid";
    public bool HasApi { get; set; } = true;
    public bool Applied { get; set; }
    public WorkspaceRecord? Workspace { get; set; }
}
