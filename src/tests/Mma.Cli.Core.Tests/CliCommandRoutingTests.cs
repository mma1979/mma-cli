using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.App;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Database;
using Spectre.Console;

namespace Mma.Cli.Core.Tests;

public sealed class CliCommandRoutingTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;

    public CliCommandRoutingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-cli-app-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _dbPath = Path.Combine(_tempRoot, "db", "workspace.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Cli_NewCommand_AliasN_ScaffoldsRegistersWorkspaceAndOutputsJson()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();
        var console = CreateTestConsole(consoleWriter);

        var exitCode = await CliApp.RunAsync(
            ["n", "MyApi", "--output", _tempRoot, "--json"],
            services,
            console,
            outputWriter,
            errorWriter,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, consoleWriter.ToString());

        using var doc = JsonDocument.Parse(outputWriter.ToString());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("new", root.GetProperty("command").GetString());
        Assert.Equal("MyApi", root.GetProperty("data").GetProperty("solutionName").GetString());

        var slnDir = Path.Combine(_tempRoot, "MyApi");
        Assert.True(File.Exists(Path.Combine(slnDir, "MyApi.sln")));
        Assert.True(File.Exists(Path.Combine(slnDir, ".mma", "project.mma")));

        await using var scope = services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
        var workspace = await registry.GetByPathAsync(slnDir);
        Assert.NotNull(workspace);
        Assert.Equal("MyApi", workspace.Name);
        Assert.Equal("AutoMapper", workspace.Mapper);
    }

    [Fact]
    public async Task Cli_NewCommand_DryRun_DoesNotWriteFilesOrRegisterWorkspace()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();
        var console = CreateTestConsole(consoleWriter);

        var exitCode = await CliApp.RunAsync(
            ["new", "DryApi", "--output", _tempRoot, "--dry-run", "--json"],
            services,
            console,
            outputWriter,
            errorWriter,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, consoleWriter.ToString());

        using var doc = JsonDocument.Parse(outputWriter.ToString());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("data").GetProperty("dryRun").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "DryApi")));

        if (File.Exists(_dbPath))
        {
            await using var scope = services.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
            await registry.EnsureInitializedAsync();
            Assert.Empty(await registry.ListAsync());
        }
    }

    [Fact]
    public async Task Cli_GenerateEntityPropertyRelation_EndToEndWithAliases()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        var slnDir = Path.Combine(_tempRoot, "ShopApi");

        Assert.Equal(0, await RunJsonCommandAsync(services, ["n", "ShopApi", "--output", _tempRoot, "--json"], _tempRoot));
        Assert.Equal(0, await RunJsonCommandAsync(services, ["g", "e", "Order", "Guid", "--json"], slnDir));
        Assert.Equal(0, await RunJsonCommandAsync(services, ["g", "e", "OrderItem", "Guid", "--json"], slnDir));
        Assert.Equal(0, await RunJsonCommandAsync(services, ["g", "p", "Order", "Total", "decimal", "false", "--json"], slnDir));
        Assert.Equal(0, await RunJsonCommandAsync(services, ["g", "r", "Order", "OrderItem", "OrderId", "Guid", "--json"], slnDir));

        await using (var scope = services.CreateAsyncScope())
        {
            var manifestStore = scope.ServiceProvider.GetRequiredService<IManifestStore>();
            var manifestResult = await manifestStore.LoadAsync(slnDir);
            Assert.True(manifestResult.IsSuccess);
            var manifest = manifestResult.Value!.Manifest;

            var order = Assert.Single(manifest.Entities, e => e.Name == "Order");
            Assert.Contains(order.Properties, p => p.Name == "Total" && p.DataType == "decimal" && !p.Nullable);
            Assert.Contains(manifest.Relations, r =>
                r.ParentEntity == "Order" && r.ChildEntity == "OrderItem" && r.ForeignKey == "OrderId");

            var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
            var workspace = await registry.GetByPathAsync(slnDir);
            Assert.NotNull(workspace);
            Assert.NotNull(await registry.FindEntityAsync(workspace.Id, "Order"));
            Assert.NotNull(await registry.FindEntityAsync(workspace.Id, "OrderItem"));
        }

        Assert.Equal(0, await RunJsonCommandAsync(services, ["g", "r", "Order", "OrderItem", "OrderId", "Guid", "--remove", "--json"], slnDir));
        Assert.Equal(0, await RunJsonCommandAsync(services, ["g", "p", "Order", "Total", "decimal", "false", "--remove", "--json"], slnDir));

        await using (var scope = services.CreateAsyncScope())
        {
            var manifestStore = scope.ServiceProvider.GetRequiredService<IManifestStore>();
            var manifestResult = await manifestStore.LoadAsync(slnDir);
            Assert.True(manifestResult.IsSuccess);
            var manifest = manifestResult.Value!.Manifest;

            var order = Assert.Single(manifest.Entities, e => e.Name == "Order");
            Assert.DoesNotContain(order.Properties, p => p.Name == "Total");
            Assert.Empty(manifest.Relations);
        }
    }

    [Fact]
    public async Task Cli_ValidateCommand_ReturnsZeroForHealthySolutionAndNonZeroOnDrift()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        var slnDir = Path.Combine(_tempRoot, "HealthyApi");

        Assert.Equal(0, await RunJsonCommandAsync(services, ["n", "HealthyApi", "--output", _tempRoot, "--json"], _tempRoot));
        Assert.Equal(0, await RunJsonCommandAsync(services, ["g", "e", "Customer", "Guid", "--json"], slnDir));

        using var healthyOut = new StringWriter();
        using var healthyErr = new StringWriter();
        using var healthyConsoleOut = new StringWriter();
        var healthyExit = await CliApp.RunAsync(
            ["validate", "--solution", slnDir, "--json"],
            services,
            CreateTestConsole(healthyConsoleOut),
            healthyOut,
            healthyErr,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(0, healthyExit);
        Assert.Equal(string.Empty, healthyConsoleOut.ToString());
        using (var healthyDoc = JsonDocument.Parse(healthyOut.ToString()))
        {
            Assert.True(healthyDoc.RootElement.GetProperty("success").GetBoolean());
            Assert.True(healthyDoc.RootElement.GetProperty("data").GetProperty("isValid").GetBoolean());
        }

        var entityFile = Path.Combine(slnDir, "HealthyApi", "HealthyApi.Core", "Database", "Tables", "Customer.cs");
        File.Delete(entityFile);

        using var driftOut = new StringWriter();
        using var driftErr = new StringWriter();
        using var driftConsoleOut = new StringWriter();
        var driftExit = await CliApp.RunAsync(
            ["validate", "--solution", slnDir, "--json"],
            services,
            CreateTestConsole(driftConsoleOut),
            driftOut,
            driftErr,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(1, driftExit);
        Assert.Equal(string.Empty, driftConsoleOut.ToString());
        using (var driftDoc = JsonDocument.Parse(driftOut.ToString()))
        {
            Assert.False(driftDoc.RootElement.GetProperty("success").GetBoolean());
            Assert.False(driftDoc.RootElement.GetProperty("data").GetProperty("isValid").GetBoolean());
            Assert.Contains(
                driftDoc.RootElement.GetProperty("data").GetProperty("issues").EnumerateArray(),
                issue => issue.GetProperty("code").GetString() == "DRIFT_MISSING_ENTITY_FILE");
        }
    }

    [Fact]
    public async Task Cli_DoctorCommand_OutputsJsonDiagnostics()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();

        var exitCode = await CliApp.RunAsync(
            ["doctor", "--json"],
            services,
            CreateTestConsole(consoleWriter),
            outputWriter,
            errorWriter,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, consoleWriter.ToString());

        var jsonText = outputWriter.ToString();
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("doctor", root.GetProperty("command").GetString());

        var data = root.GetProperty("data");
        Assert.True(data.TryGetProperty("dotnetSdk", out var sdk));
        Assert.True(sdk.GetProperty("available").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(sdk.GetProperty("version").GetString()));
        Assert.True(data.TryGetProperty("workspaceDbPath", out var dbProp));
        Assert.False(string.IsNullOrWhiteSpace(dbProp.GetString()));
    }

    [Fact]
    public async Task Cli_InvalidSolutionPath_ReturnsNonZeroWithoutCrashingProcess()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        var missingDir = Path.Combine(_tempRoot, "missing-solution-dir");
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();

        var exitCode = await CliApp.RunAsync(
            ["g", "e", "BadEntity", "--solution", missingDir, "--json"],
            services,
            CreateTestConsole(consoleWriter),
            outputWriter,
            errorWriter,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, consoleWriter.ToString());

        using var doc = JsonDocument.Parse(outputWriter.ToString());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(ErrorCodes.SOLUTION_NOT_FOUND, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cli_SpecialCharacterPathWithBrackets_EscapesSpectreMarkupCleanly()
    {
        var bracketedDir = Path.Combine(_tempRoot, "mma-[temp]-bracket");
        Directory.CreateDirectory(bracketedDir);

        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();
        var console = CreateTestConsole(consoleWriter);

        var createExit = await CliApp.RunAsync(
            ["new", "BracketApi", "--output", bracketedDir],
            services,
            console,
            outputWriter,
            errorWriter,
            () => bracketedDir,
            isInteractive: false);

        Assert.Equal(0, createExit);

        var slnDir = Path.Combine(bracketedDir, "BracketApi");
        Assert.Equal(0, await CliApp.RunAsync(["g", "e", "Customer", "--solution", slnDir], services, console, outputWriter, errorWriter, () => bracketedDir, isInteractive: false));

        var validateExit = await CliApp.RunAsync(
            ["validate", "--solution", slnDir],
            services,
            console,
            outputWriter,
            errorWriter,
            () => bracketedDir,
            isInteractive: false);

        Assert.Equal(0, validateExit);

        // Delete entity file so ValidationIssue.Message contains bracketed path and test Spectre table escaping
        File.Delete(Path.Combine(slnDir, "BracketApi", "BracketApi.Core", "Database", "Tables", "Customer.cs"));
        var driftHumanExit = await CliApp.RunAsync(
            ["validate", "--solution", slnDir],
            services,
            console,
            outputWriter,
            errorWriter,
            () => bracketedDir,
            isInteractive: false);

        Assert.Equal(1, driftHumanExit);
        Assert.Contains("DRIFT_MISSING_ENTITY_FILE", consoleWriter.ToString(), StringComparison.Ordinal);

        var doctorHumanExit = await CliApp.RunAsync(
            ["doctor", "--solution", slnDir],
            services,
            console,
            outputWriter,
            errorWriter,
            () => bracketedDir,
            isInteractive: false);

        Assert.Equal(1, doctorHumanExit);

        var missingWithBrackets = Path.Combine(bracketedDir, "missing-[dir]");
        var missingExit = await CliApp.RunAsync(
            ["g", "e", "Order", "--solution", missingWithBrackets],
            services,
            console,
            outputWriter,
            errorWriter,
            () => bracketedDir,
            isInteractive: false);

        Assert.Equal(1, missingExit);
        Assert.Contains("missing-[dir]", consoleWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_WatchCommand_WhenCancelledCleanly_ReturnsZeroAndEmitsJson()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        Assert.Equal(0, await RunJsonCommandAsync(services, ["n", "WatchApi", "--output", _tempRoot, "--json"], _tempRoot));
        var slnDir = Path.Combine(_tempRoot, "WatchApi");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        using var watchOut = new StringWriter();
        using var watchErr = new StringWriter();
        using var watchConsole = new StringWriter();

        var exitCode = await CliApp.RunAsync(
            ["watch", slnDir, "--json"],
            services,
            CreateTestConsole(watchConsole),
            watchOut,
            watchErr,
            () => _tempRoot,
            isInteractive: false,
            ct: cts.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, watchConsole.ToString());
        using var doc = JsonDocument.Parse(watchOut.ToString());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("watch", doc.RootElement.GetProperty("command").GetString());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("stopped").GetBoolean());
    }

    [Fact]
    public async Task Cli_ImportCommand_WhenEndpointUnreachableOrMissingManifest_ReturnsNonZeroJson()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();

        var exitCode = await CliApp.RunAsync(
            ["import", Path.Combine(_tempRoot, "nonexistent"), "--json"],
            services,
            CreateTestConsole(consoleWriter),
            outputWriter,
            errorWriter,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, consoleWriter.ToString());
        using var doc = JsonDocument.Parse(outputWriter.ToString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(ErrorCodes.SOLUTION_NOT_FOUND, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cli_GenerateEntity_WithNoApiAndMapster_RespectsOptions()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        var slnDir = Path.Combine(_tempRoot, "MapsterCliApi");
        Assert.Equal(0, await RunJsonCommandAsync(services, ["n", "MapsterCliApi", "--mapper", "Mapster", "--output", _tempRoot, "--json"], _tempRoot));
        Assert.Equal(0, await RunJsonCommandAsync(services, ["g", "e", "Invoice", "long", "--no-api", "--json"], slnDir));

        var projectsDir = Path.Combine(slnDir, "MapsterCliApi");
        Assert.True(File.Exists(Path.Combine(projectsDir, "MapsterCliApi.Core", "Models", "InvoiceReadModel.g.cs")));
        Assert.False(File.Exists(Path.Combine(projectsDir, "MapsterCliApi.AppApi", "Controllers", "v1", "InvoicesController.cs")));
    }

    [Fact]
    public async Task Cli_ZeroArgsNonInteractive_RendersHelpAndReturnsZero()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();

        var exitCode = await CliApp.RunAsync(
            [],
            services,
            CreateTestConsole(consoleWriter),
            outputWriter,
            errorWriter,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(0, exitCode);
        Assert.Contains("new", outputWriter.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generate", outputWriter.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validate", outputWriter.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("doctor", outputWriter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cli_WatchAndUiCommands_HandleMissingPathsGracefully()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using var watchOut = new StringWriter();
        using var watchErr = new StringWriter();
        using var watchConsole = new StringWriter();

        var watchExit = await CliApp.RunAsync(
            ["watch", Path.Combine(_tempRoot, "no-solution"), "--json"],
            services,
            CreateTestConsole(watchConsole),
            watchOut,
            watchErr,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(1, watchExit);
        using (var watchDoc = JsonDocument.Parse(watchOut.ToString()))
        {
            Assert.False(watchDoc.RootElement.GetProperty("success").GetBoolean());
        }

        using var uiOut = new StringWriter();
        using var uiErr = new StringWriter();
        using var uiConsole = new StringWriter();

        var uiExit = await CliApp.RunAsync(
            ["ui", "--port", "5180", "--json"],
            services,
            CreateTestConsole(uiConsole),
            uiOut,
            uiErr,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(1, uiExit);
        using (var uiDoc = JsonDocument.Parse(uiOut.ToString()))
        {
            Assert.False(uiDoc.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public void AppAssemblyAndSources_EnforceCleanArchitectureBoundaries()
    {
        Assembly appAssembly = typeof(CliApp).Assembly;
        var referencedNames = appAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var forbidden in new[] { "Mma.Cli.Shared", "Mma.Cli.UI", "Newtonsoft.Json", "RestSharp", "Sharprompt" })
        {
            Assert.DoesNotContain(forbidden, referencedNames);
        }

        var repoRoot = FindRepoRoot();
        var appDir = Path.Combine(repoRoot, "src", "Mma.Cli.App");
        foreach (var file in Directory.GetFiles(appDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            var raw = File.ReadAllText(file);
            Assert.DoesNotContain("Environment.Exit", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("Process.GetProcessesByName", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("Console.Clear", raw, StringComparison.Ordinal);
        }
    }

    private static async Task<int> RunJsonCommandAsync(IServiceProvider services, string[] args, string cwd)
    {
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();
        var exitCode = await CliApp.RunAsync(
            args,
            services,
            CreateTestConsole(consoleWriter),
            outputWriter,
            errorWriter,
            () => cwd,
            isInteractive: false);

        Assert.Equal(string.Empty, consoleWriter.ToString());
        using var _ = JsonDocument.Parse(outputWriter.ToString());
        return exitCode;
    }

    private static IAnsiConsole CreateTestConsole(StringWriter writer) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer)
        });

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mma.Cli.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Mma.Cli.sln.");
    }
}
