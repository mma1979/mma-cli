using System.Text;

using CliWrap;

namespace Mma.Cli.Bff.CodeGen;

public sealed record EfMigrationResult(bool Succeeded, string Output, string? Error);

public interface IEfMigrationRunner
{
    Task<EfMigrationResult> AddMigrationAsync(
        string solutionPath,
        string solutionName,
        string migrationName,
        CancellationToken cancellationToken);
}

public sealed class EfMigrationRunner : IEfMigrationRunner
{
    public async Task<EfMigrationResult> AddMigrationAsync(
        string solutionPath,
        string solutionName,
        string migrationName,
        CancellationToken cancellationToken)
    {
        var efPath = Path.Combine(solutionPath, solutionName, $"{solutionName}.EntityFramework");
        var apiPath = Path.Combine(solutionPath, solutionName, $"{solutionName}.AppApi");

        if (!Directory.Exists(efPath) || !Directory.Exists(apiPath))
        {
            return new EfMigrationResult(
                false,
                string.Empty,
                $"EF Core project directories were not found under '{Path.Combine(solutionPath, solutionName)}'.");
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try
        {
            var result = await CliWrap.Cli.Wrap("dotnet")
                .WithWorkingDirectory(efPath)
                .WithArguments(a => a
                    .Add("ef").Add("migrations").Add("add").Add(migrationName)
                    .Add("--context").Add("ApplicationDbContext")
                    .Add("--startup-project").Add(apiPath))
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
                .ExecuteAsync(cancellationToken);

            return new EfMigrationResult(
                result.ExitCode == 0,
                stdout.ToString().Trim(),
                result.ExitCode == 0 ? null : stderr.ToString().Trim());
        }
        catch (Exception ex)
        {
            return new EfMigrationResult(false, stdout.ToString().Trim(), ex.Message);
        }
    }
}
