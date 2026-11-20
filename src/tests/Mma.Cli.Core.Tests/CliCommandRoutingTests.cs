using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mma.Cli.App;
using Mma.Cli.App.Tui;
using Mma.Cli.Core.Export;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Database;
using Mma.Cli.Database.Entities;
using Spectre.Console;
using Spectre.Console.Rendering;

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
        Assert.False(File.Exists(_dbPath));
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

    [Fact]
    public async Task InteractiveMenu_GenerateEntityAndExit_EscapesByteArrayAndBracketedDefaultPath()
    {
        var bracketedDir = Path.Combine(_tempRoot, "tui-[bracket]");
        Directory.CreateDirectory(bracketedDir);

        await using var services = CliApp.BuildServiceProvider(_dbPath);
        Assert.Equal(0, await RunJsonCommandAsync(services, ["n", "MenuApi", "--output", bracketedDir, "--json"], bracketedDir));
        var slnDir = Path.Combine(bracketedDir, "MenuApi");

        var scriptedInput = new ScriptedAnsiConsoleInput();
        // 1. Select "Generate Entity" (index 1)
        scriptedInput.PushKey(ConsoleKey.DownArrow);
        scriptedInput.PushKey(ConsoleKey.Enter);
        // 2. Accept default solution path (slnDir containing "[bracket]")
        scriptedInput.PushKey(ConsoleKey.Enter);
        // 3. Enter entity name "Widget"
        scriptedInput.PushText("Widget");
        scriptedInput.PushKey(ConsoleKey.Enter);
        // 4. Select "Guid" (index 0) from DataTypes.All (which renders "byte[]")
        scriptedInput.PushKey(ConsoleKey.Enter);
        // 5. Confirm "Generate API controller?" default (Yes)
        scriptedInput.PushKey(ConsoleKey.Enter);
        // 6. Select "Exit" (index 8 from 9 choices)
        for (var i = 0; i < 8; i++)
        {
            scriptedInput.PushKey(ConsoleKey.DownArrow);
        }
        scriptedInput.PushKey(ConsoleKey.Enter);

        using var consoleWriter = new StringWriter();
        using var outputWriter = new StringWriter();
        var console = new ScriptedAnsiConsole(consoleWriter, scriptedInput);

        var exitCode = await InteractiveMenu.RunAsync(
            services,
            console,
            outputWriter,
            () => slnDir);

        Assert.Equal(0, exitCode);
        Assert.Contains("byte[]", consoleWriter.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(slnDir, "MenuApi", "MenuApi.Core", "Database", "Tables", "Widget.cs")));
    }

    [Fact]
    public async Task WatchCommand_WhenManifestChanges_DebouncesAndInvokesExporter()
    {
        await using var baseServices = CliApp.BuildServiceProvider(_dbPath);
        Assert.Equal(0, await RunJsonCommandAsync(baseServices, ["n", "SpyWatchApi", "--output", _tempRoot, "--json"], _tempRoot));
        var slnDir = Path.Combine(_tempRoot, "SpyWatchApi");

        var spyExporter = new SpySolutionExporter();
        var collection = new ServiceCollection();
        collection.AddMmaCore();
        collection.AddMmaDatabase(_dbPath);
        collection.Replace(ServiceDescriptor.Transient<ISolutionExporter>(_ => spyExporter));
        await using var services = collection.BuildServiceProvider();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var watchOut = new StringWriter();
        using var watchErr = new StringWriter();
        using var watchConsole = new StringWriter();

        var watchTask = CliApp.RunAsync(
            ["watch", slnDir, "--json"],
            services,
            CreateTestConsole(watchConsole),
            watchOut,
            watchErr,
            () => _tempRoot,
            isInteractive: false,
            ct: cts.Token);

        await Task.Delay(200);

        var manifestPath = Path.Combine(slnDir, ".mma", "project.mma");
        var tempManifestPath = Path.Combine(slnDir, ".mma", $"project.mma.{Guid.NewGuid():N}.tmp");
        var existingContent = await File.ReadAllTextAsync(manifestPath);
        await File.WriteAllTextAsync(tempManifestPath, existingContent + " ");
        File.Move(tempManifestPath, manifestPath, overwrite: true);

        var exportedSolution = await spyExporter.FirstExportTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(Path.GetFullPath(slnDir), Path.GetFullPath(exportedSolution));

        await cts.CancelAsync();
        var exitCode = await watchTask;

        Assert.Equal(0, exitCode);
        Assert.True(spyExporter.InvocationCount >= 1);
        Assert.Equal(string.Empty, watchConsole.ToString());

        using var doc = JsonDocument.Parse(watchOut.ToString());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("exportCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Cli_ParseErrorWithJson_ReturnsOneAndValidationErrorJsonEnvelope()
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();

        var exitCode = await CliApp.RunAsync(
            ["new", "--json"],
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
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("0", ErrorCodes.VALIDATION_ERROR)]
    [InlineData("70000", ErrorCodes.VALIDATION_ERROR)]
    [InlineData("5180", ErrorCodes.IO_ERROR)]
    public async Task Cli_UiCommand_InvalidPortAndMissingBinary_ReturnsOneWithStructuredJson(string port, string expectedCode)
    {
        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        using var consoleWriter = new StringWriter();

        var exitCode = await CliApp.RunAsync(
            ["ui", "--port", port, "--json"],
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
        Assert.Equal("ui", root.GetProperty("command").GetString());
        Assert.Equal(expectedCode, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cli_ImportCommand_MissingManifestAndUnreachableEndpoint_ReturnsErrorCodes()
    {
        var slnDir = Path.Combine(_tempRoot, "ImportCheckApi");
        Directory.CreateDirectory(slnDir);
        await File.WriteAllTextAsync(Path.Combine(slnDir, "ImportCheckApi.sln"), string.Empty);

        await using var services = CliApp.BuildServiceProvider(_dbPath);
        using (var missingOut = new StringWriter())
        using (var missingErr = new StringWriter())
        using (var missingConsole = new StringWriter())
        {
            var missingExit = await CliApp.RunAsync(
                ["import", slnDir, "--json"],
                services,
                CreateTestConsole(missingConsole),
                missingOut,
                missingErr,
                () => _tempRoot,
                isInteractive: false);

            Assert.Equal(1, missingExit);
            Assert.Equal(string.Empty, missingConsole.ToString());
            using var missingDoc = JsonDocument.Parse(missingOut.ToString());
            Assert.False(missingDoc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(ErrorCodes.IO_ERROR, missingDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        var mmaDir = Path.Combine(slnDir, ".mma");
        Directory.CreateDirectory(mmaDir);
        await File.WriteAllTextAsync(Path.Combine(mmaDir, "project.mma"), """{"schemaVersion":2,"solutionName":"ImportCheckApi"}""");
        await File.WriteAllTextAsync(Path.Combine(mmaDir, "config.json"), """{"GeneratorEndpoint":"http://127.0.0.1:59999"}""");

        var collection = new ServiceCollection();
        collection.AddMmaCore();
        collection.AddMmaDatabase(_dbPath);
        collection.Replace(ServiceDescriptor.Singleton<IHttpClientFactory>(new FailingHttpClientFactory()));
        await using var failingServices = collection.BuildServiceProvider();

        using var httpOut = new StringWriter();
        using var httpErr = new StringWriter();
        using var httpConsole = new StringWriter();
        var httpExit = await CliApp.RunAsync(
            ["import", slnDir, "--json"],
            failingServices,
            CreateTestConsole(httpConsole),
            httpOut,
            httpErr,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(1, httpExit);
        Assert.Equal(string.Empty, httpConsole.ToString());
        using var httpDoc = JsonDocument.Parse(httpOut.ToString());
        Assert.False(httpDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(ErrorCodes.HTTP_ERROR, httpDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cli_RegistryFailure_EmitsWarningInJsonAndHumanOutputWithoutFailingScaffold()
    {
        var collection = new ServiceCollection();
        collection.AddMmaCore();
        collection.AddMmaDatabase(_dbPath);
        collection.Replace(ServiceDescriptor.Scoped<IWorkspaceRegistry>(_ => new ThrowingWorkspaceRegistry()));
        await using var services = collection.BuildServiceProvider();

        using (var newOut = new StringWriter())
        using (var newErr = new StringWriter())
        using (var newConsole = new StringWriter())
        {
            var newExit = await CliApp.RunAsync(
                ["n", "WarnApi", "--output", _tempRoot, "--json"],
                services,
                CreateTestConsole(newConsole),
                newOut,
                newErr,
                () => _tempRoot,
                isInteractive: false);

            Assert.Equal(0, newExit);
            Assert.True(File.Exists(Path.Combine(_tempRoot, "WarnApi", "WarnApi.sln")));
            using var doc = JsonDocument.Parse(newOut.ToString());
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(
                doc.RootElement.GetProperty("warnings").EnumerateArray(),
                w => w.GetString()!.Contains("simulated sqlite failure", StringComparison.Ordinal));
        }

        var slnDir = Path.Combine(_tempRoot, "WarnApi");
        using var entityOut = new StringWriter();
        using var entityErr = new StringWriter();
        using var entityConsole = new StringWriter();
        var entityExit = await CliApp.RunAsync(
            ["g", "e", "Ticket", "--solution", slnDir],
            services,
            CreateTestConsole(entityConsole),
            entityOut,
            entityErr,
            () => _tempRoot,
            isInteractive: false);

        Assert.Equal(0, entityExit);
        Assert.True(File.Exists(Path.Combine(slnDir, "WarnApi", "WarnApi.Core", "Database", "Tables", "Ticket.cs")));
        Assert.Contains("Warning:", entityConsole.ToString(), StringComparison.Ordinal);
        Assert.Contains("simulated sqlite failure", entityConsole.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DoctorCommand_WhenDbThrows_KeepsCleanWorkspaceDbPathAndReportsError()
    {
        var collection = new ServiceCollection();
        collection.AddMmaCore();
        collection.AddMmaDatabase(_dbPath);
        collection.Replace(ServiceDescriptor.Scoped<IWorkspaceRegistry>(_ => new ThrowingWorkspaceRegistry()));
        await using var services = collection.BuildServiceProvider();

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

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, consoleWriter.ToString());

        using var doc = JsonDocument.Parse(outputWriter.ToString());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        var data = root.GetProperty("data");
        Assert.False(data.GetProperty("workspaceDbAccessible").GetBoolean());
        Assert.Equal("Error", data.GetProperty("workspaceDbStatus").GetString());
        Assert.Equal(_dbPath, data.GetProperty("workspaceDbPath").GetString());
        Assert.Contains("simulated sqlite failure", data.GetProperty("workspaceDbMessage").GetString(), StringComparison.Ordinal);
    }

    private sealed class SpySolutionExporter : ISolutionExporter
    {
        private readonly TaskCompletionSource<string> _firstExport = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _invocationCount;

        public Task<string> FirstExportTask => _firstExport.Task;
        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public Task<OperationResult> ExportAsync(string solutionPath, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _invocationCount);
            _firstExport.TrySetResult(solutionPath);
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FailingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHttpHandler());

        private sealed class ThrowingHttpHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Simulated connection refused.");
        }
    }

    private sealed class ThrowingWorkspaceRegistry : IWorkspaceRegistry
    {
        public Task EnsureInitializedAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated sqlite failure");

        public Task<WorkspaceRecord> RegisterAsync(string name, string solutionPath, string mapper, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated sqlite failure");

        public Task<IReadOnlyList<WorkspaceRecord>> ListAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated sqlite failure");

        public Task<WorkspaceRecord?> GetByIdAsync(int workspaceId, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated sqlite failure");

        public Task<WorkspaceRecord?> GetByPathAsync(string solutionPath, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated sqlite failure");

        public Task<OperationResult<WorkspaceRecord>> SyncFromManifestAsync(int workspaceId, ProjectManifest manifest, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated sqlite failure");

        public Task<WorkspaceEntityRecord?> FindEntityAsync(int workspaceId, string entityName, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated sqlite failure");

        public Task<bool> RemoveAsync(int workspaceId, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated sqlite failure");
    }

    private sealed class ScriptedAnsiConsoleInput : IAnsiConsoleInput
    {
        private readonly Queue<ConsoleKeyInfo> _keys = new();

        public void PushKey(ConsoleKey key)
        {
            var ch = key == ConsoleKey.Enter ? '\r' : '\0';
            _keys.Enqueue(new ConsoleKeyInfo(ch, key, shift: false, alt: false, control: false));
        }

        public void PushText(string text)
        {
            foreach (var ch in text)
            {
                var key = char.ToUpperInvariant(ch) switch
                {
                    >= 'A' and <= 'Z' => (ConsoleKey)char.ToUpperInvariant(ch),
                    _ => ConsoleKey.NoName
                };
                _keys.Enqueue(new ConsoleKeyInfo(ch, key, char.IsUpper(ch), alt: false, control: false));
            }
        }

        public bool IsKeyAvailable() => _keys.Count > 0;

        public ConsoleKeyInfo? ReadKey(bool intercept) =>
            _keys.Count > 0 ? _keys.Dequeue() : null;

        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReadKey(intercept));
        }
    }

    private sealed class ScriptedAnsiConsole : IAnsiConsole
    {
        private readonly IAnsiConsole _inner;

        public ScriptedAnsiConsole(TextWriter writer, IAnsiConsoleInput input)
        {
            _inner = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.Yes,
                Out = new AnsiConsoleOutput(writer)
            });
            Input = input;
        }

        public Profile Profile => _inner.Profile;
        public IAnsiConsoleCursor Cursor => _inner.Cursor;
        public IAnsiConsoleInput Input { get; }
        public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;
        public RenderPipeline Pipeline => _inner.Pipeline;
        public void Clear(bool home) => _inner.Clear(home);
        public void Write(IRenderable renderable) => _inner.Write(renderable);
        public void WriteAnsi(Action<AnsiWriter> action) => _inner.WriteAnsi(action);
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
