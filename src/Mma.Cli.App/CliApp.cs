using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mma.Cli.App.Commands;
using Mma.Cli.App.Output;
using Mma.Cli.App.Tui;
using Mma.Cli.Core;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Database;
using Spectre.Console;

namespace Mma.Cli.App;

public static class CliApp
{
    public static ServiceProvider BuildServiceProvider(string? dbPath = null, bool enableConsoleLogging = false)
    {
        var services = new ServiceCollection();

        if (enableConsoleLogging)
        {
            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Warning);
                logging.AddConsole(options =>
                {
                    options.LogToStandardErrorThreshold = LogLevel.Warning;
                });
            });
        }

        services.AddMmaCore();
        services.AddMmaDatabase(dbPath);

        return services.BuildServiceProvider();
    }

    public static RootCommand BuildRootCommand(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter? outputWriter = null,
        Func<string>? currentDirectoryProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);

        var effectiveOutput = outputWriter ?? Console.Out;
        var effectiveCwd = currentDirectoryProvider ?? Directory.GetCurrentDirectory;

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit machine-readable JSON output.",
            Recursive = true
        };

        var rootCommand = new RootCommand("MMA Clean Architecture & DDD Scaffolding CLI");
        rootCommand.Options.OfType<VersionOption>().FirstOrDefault()?.Aliases.Add("-v");
        rootCommand.Add(jsonOption);

        rootCommand.Add(NewCommand.Create(services, console, effectiveOutput, effectiveCwd, jsonOption));
        rootCommand.Add(GenerateCommand.Create(services, console, effectiveOutput, effectiveCwd, jsonOption));
        rootCommand.Add(ValidateCommand.Create(services, console, effectiveOutput, effectiveCwd, jsonOption));
        rootCommand.Add(ImportCommand.Create(services, console, effectiveOutput, effectiveCwd, jsonOption));
        rootCommand.Add(WatchCommand.Create(services, console, effectiveOutput, effectiveCwd, jsonOption));
        rootCommand.Add(DoctorCommand.Create(services, console, effectiveOutput, effectiveCwd, jsonOption));
        rootCommand.Add(UiCommand.Create(console, effectiveOutput, jsonOption));

        return rootCommand;
    }

    public static async Task<int> RunAsync(
        string[] args,
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter? outputWriter = null,
        TextWriter? errorWriter = null,
        Func<string>? currentDirectoryProvider = null,
        bool? isInteractive = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);

        var effectiveOutput = outputWriter ?? Console.Out;
        var effectiveError = errorWriter ?? Console.Error;
        var effectiveCwd = currentDirectoryProvider ?? Directory.GetCurrentDirectory;

        var rootCommand = BuildRootCommand(services, console, effectiveOutput, effectiveCwd);
        var invocationConfig = new InvocationConfiguration
        {
            Output = effectiveOutput,
            Error = effectiveError
        };

        if (args.Length == 0)
        {
            var interactive = isInteractive ?? !Console.IsInputRedirected;
            if (interactive)
            {
                return await InteractiveMenu.RunAsync(
                    services,
                    console,
                    effectiveOutput,
                    effectiveCwd,
                    ct).ConfigureAwait(false);
            }

            return await rootCommand.Parse(["--help"]).InvokeAsync(invocationConfig, ct).ConfigureAwait(false);
        }

        var parseResult = rootCommand.Parse(args);
        if (parseResult.Errors.Count > 0 && args.Contains("--json", StringComparer.OrdinalIgnoreCase))
        {
            var message = string.Join("; ", parseResult.Errors.Select(e => e.Message));
            CliOutputFormatter.WriteFailure(
                console,
                effectiveOutput,
                json: true,
                parseResult.CommandResult.Command.Name,
                ErrorCodes.VALIDATION_ERROR,
                message);
            return 1;
        }

        return await parseResult.InvokeAsync(invocationConfig, ct).ConfigureAwait(false);
    }

    internal static string ResolvePath(string? explicitPath, Func<string> currentDirectoryProvider)
    {
        var baseDir = currentDirectoryProvider();
        return string.IsNullOrWhiteSpace(explicitPath)
            ? Path.GetFullPath(baseDir)
            : Path.GetFullPath(explicitPath, baseDir);
    }

    internal static async Task SyncWorkspaceFromManifestAsync(
        IServiceProvider services,
        string solutionPath,
        CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var locator = scope.ServiceProvider.GetRequiredService<ISolutionLocator>();
        var manifestStore = scope.ServiceProvider.GetRequiredService<IManifestStore>();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();

        var located = locator.Locate(solutionPath);
        var effectiveSolutionPath = located.IsSuccess && located.Value is not null
            ? located.Value.SolutionPath
            : Path.GetFullPath(solutionPath);

        var manifestResult = await manifestStore.LoadAsync(effectiveSolutionPath, ct).ConfigureAwait(false);
        if (!manifestResult.IsSuccess || manifestResult.Value is null)
        {
            return;
        }

        await registry.EnsureInitializedAsync(ct).ConfigureAwait(false);
        var workspace = await registry.GetByPathAsync(effectiveSolutionPath, ct).ConfigureAwait(false);
        if (workspace is null && located.IsSuccess && located.Value is not null)
        {
            workspace = await registry.RegisterAsync(
                located.Value.SolutionName,
                effectiveSolutionPath,
                manifestResult.Value.Manifest.Mapper,
                ct).ConfigureAwait(false);
        }

        if (workspace is not null)
        {
            await registry.SyncFromManifestAsync(workspace.Id, manifestResult.Value.Manifest, ct).ConfigureAwait(false);
        }
    }
}
