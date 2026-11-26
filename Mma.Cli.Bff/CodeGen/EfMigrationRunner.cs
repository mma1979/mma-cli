using System.Text;

using CliWrap;

using Mma.Cli.Shared.Helpers;

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
    public static (string ProjectsPath, string EfPath, string ApiPath) ResolveMigrationPaths(
        string solutionPath,
        string solutionName)
    {
        var effectiveName = solutionName;
        var projectsPath = BuildHelper.ResolveProjectsPath(solutionPath, effectiveName);
        var efPath = Path.Combine(projectsPath, $"{effectiveName}.EntityFramework");
        var apiPath = Path.Combine(projectsPath, $"{effectiveName}.AppApi");

        if ((!Directory.Exists(efPath) || !Directory.Exists(apiPath)) && Directory.Exists(solutionPath))
        {
            try
            {
                var (detectedName, detectedProjectsPath) = BuildHelper.CheckSolutionPath(solutionPath);
                var candidateEf = Path.Combine(detectedProjectsPath, $"{detectedName}.EntityFramework");
                var candidateApi = Path.Combine(detectedProjectsPath, $"{detectedName}.AppApi");
                if (Directory.Exists(candidateEf) && Directory.Exists(candidateApi))
                {
                    return (detectedProjectsPath, candidateEf, candidateApi);
                }
            }
            catch
            {
                // Fall back to the initial candidate paths so the caller gets a clear not-found error.
            }
        }

        return (projectsPath, efPath, apiPath);
    }

    public async Task<EfMigrationResult> AddMigrationAsync(
        string solutionPath,
        string solutionName,
        string migrationName,
        CancellationToken cancellationToken)
    {
        var (projectsPath, efPath, apiPath) = ResolveMigrationPaths(solutionPath, solutionName);

        if (!Directory.Exists(efPath) || !Directory.Exists(apiPath))
        {
            return new EfMigrationResult(
                false,
                string.Empty,
                $"EF Core project directories were not found under '{projectsPath}' (expected '{Path.GetFileName(efPath)}' and '{Path.GetFileName(apiPath)}').");
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
                    .Add("--project").Add(efPath)
                    .Add("--startup-project").Add(apiPath))
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
                .ExecuteAsync(cancellationToken);

            var outText = stdout.ToString().Trim();
            var errText = stderr.ToString().Trim();
            var error = result.ExitCode == 0
                ? null
                : !string.IsNullOrWhiteSpace(errText)
                    ? errText
                    : !string.IsNullOrWhiteSpace(outText)
                        ? outText
                        : $"dotnet ef exited with code {result.ExitCode}.";

            return new EfMigrationResult(
                result.ExitCode == 0,
                outText,
                error);
        }
        catch (Exception ex)
        {
            return new EfMigrationResult(false, stdout.ToString().Trim(), ex.Message);
        }
    }
}
