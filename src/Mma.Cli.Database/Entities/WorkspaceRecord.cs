namespace Mma.Cli.Database.Entities;

public sealed class WorkspaceRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SolutionPath { get; set; } = string.Empty;
    public string Mapper { get; set; } = "AutoMapper";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastOpenedAtUtc { get; set; }
    public List<WorkspaceEntityRecord> Entities { get; set; } = [];
}
