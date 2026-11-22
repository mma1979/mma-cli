using Mma.Cli.App.Commands;
using Mma.Cli.Core.Domain;
using Spectre.Console;

namespace Mma.Cli.App.Tui;

public static class InteractiveMenu
{
    private const string ActionNewSolution = "New Solution";
    private const string ActionGenerateEntity = "Generate Entity";
    private const string ActionGenerateProperty = "Generate Property";
    private const string ActionGenerateRelation = "Generate Relation";
    private const string ActionValidateSolution = "Validate Solution";
    private const string ActionDoctor = "Doctor";
    private const string ActionWatch = "Watch";
    private const string ActionLaunchUi = "Launch UI";
    private const string ActionExit = "Exit";

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(outputWriter);
        ArgumentNullException.ThrowIfNull(currentDirectoryProvider);

        console.Write(new Rule("[bold cyan]MMA CLI[/]").LeftJustified());

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var choice = await console.PromptAsync(
                    new SelectionPrompt<string>()
                        .Title("[bold]Select an action[/]")
                        .AddChoices(
                            ActionNewSolution,
                            ActionGenerateEntity,
                            ActionGenerateProperty,
                            ActionGenerateRelation,
                            ActionValidateSolution,
                            ActionDoctor,
                            ActionWatch,
                            ActionLaunchUi,
                            ActionExit),
                    ct).ConfigureAwait(false);

                switch (choice)
                {
                    case ActionNewSolution:
                        await PromptNewSolutionAsync(services, console, outputWriter, currentDirectoryProvider, ct).ConfigureAwait(false);
                        break;
                    case ActionGenerateEntity:
                        await PromptGenerateEntityAsync(services, console, outputWriter, currentDirectoryProvider, ct).ConfigureAwait(false);
                        break;
                    case ActionGenerateProperty:
                        await PromptGeneratePropertyAsync(services, console, outputWriter, currentDirectoryProvider, ct).ConfigureAwait(false);
                        break;
                    case ActionGenerateRelation:
                        await PromptGenerateRelationAsync(services, console, outputWriter, currentDirectoryProvider, ct).ConfigureAwait(false);
                        break;
                    case ActionValidateSolution:
                        await PromptValidateSolutionAsync(services, console, outputWriter, currentDirectoryProvider, ct).ConfigureAwait(false);
                        break;
                    case ActionDoctor:
                        await DoctorCommand.ExecuteAsync(
                            services,
                            console,
                            outputWriter,
                            currentDirectoryProvider,
                            solutionRaw: null,
                            json: false,
                            ct).ConfigureAwait(false);
                        break;
                    case ActionWatch:
                        await PromptWatchAsync(services, console, outputWriter, currentDirectoryProvider, ct).ConfigureAwait(false);
                        break;
                    case ActionLaunchUi:
                        await PromptLaunchUiAsync(console, outputWriter, ct).ConfigureAwait(false);
                        break;
                    case ActionExit:
                        return 0;
                }
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }

        return 0;
    }

    private static async Task PromptNewSolutionAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        CancellationToken ct)
    {
        var solutionName = await console.PromptAsync(
            new TextPrompt<string>("Solution name:")
                .Validate(name => NamingHelper.IsValidSolutionName(name)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Enter dot-separated valid C# identifiers.[/]")),
            ct).ConfigureAwait(false);

        var mapper = await console.PromptAsync(
            new SelectionPrompt<string>()
                .Title("Select mapper:")
                .AddChoices("AutoMapper", "Mapster"),
            ct).ConfigureAwait(false);

        var outputDir = await PromptDirectoryPathAsync(
            console,
            "Parent directory",
            currentDirectoryProvider(),
            ct).ConfigureAwait(false);

        await NewCommand.ExecuteAsync(
            services,
            console,
            outputWriter,
            currentDirectoryProvider,
            solutionName,
            mapper,
            outputDir,
            dryRun: false,
            json: false,
            ct).ConfigureAwait(false);
    }

    private static async Task PromptGenerateEntityAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        CancellationToken ct)
    {
        var solutionDir = await PromptSolutionPathAsync(console, currentDirectoryProvider, ct).ConfigureAwait(false);
        var entityName = await PromptIdentifierAsync(console, "Entity name:", ct).ConfigureAwait(false);
        var pkType = await PromptDataTypeAsync(console, "Primary key type:", ct).ConfigureAwait(false);
        var generateApi = await console.ConfirmAsync("Generate API controller?", defaultValue: true, ct).ConfigureAwait(false);

        await GenerateCommand.ExecuteEntityAsync(
            services,
            console,
            outputWriter,
            currentDirectoryProvider,
            entityName,
            pkType,
            mapperRaw: null,
            noApi: !generateApi,
            solutionRaw: solutionDir,
            dryRun: false,
            json: false,
            ct).ConfigureAwait(false);
    }

    private static async Task PromptGeneratePropertyAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        CancellationToken ct)
    {
        var solutionDir = await PromptSolutionPathAsync(console, currentDirectoryProvider, ct).ConfigureAwait(false);
        var entityName = await PromptIdentifierAsync(console, "Entity name:", ct).ConfigureAwait(false);
        var propertyName = await PromptIdentifierAsync(console, "Property name:", ct).ConfigureAwait(false);
        var dataType = await PromptDataTypeAsync(console, "Property data type:", ct).ConfigureAwait(false);
        var nullable = await console.ConfirmAsync("Is property nullable?", defaultValue: false, ct).ConfigureAwait(false);
        var remove = await console.ConfirmAsync("Remove this property instead of adding?", defaultValue: false, ct).ConfigureAwait(false);

        await GenerateCommand.ExecutePropertyAsync(
            services,
            console,
            outputWriter,
            currentDirectoryProvider,
            entityName,
            propertyName,
            dataType,
            nullable,
            remove,
            solutionDir,
            dryRun: false,
            json: false,
            ct).ConfigureAwait(false);
    }

    private static async Task PromptGenerateRelationAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        CancellationToken ct)
    {
        var solutionDir = await PromptSolutionPathAsync(console, currentDirectoryProvider, ct).ConfigureAwait(false);
        var parentEntity = await PromptIdentifierAsync(console, "Parent (one) entity name:", ct).ConfigureAwait(false);
        var childEntity = await PromptIdentifierAsync(console, "Child (many) entity name:", ct).ConfigureAwait(false);
        var foreignKey = await PromptIdentifierAsync(console, "Foreign key property name:", ct).ConfigureAwait(false);
        var fkType = await PromptDataTypeAsync(console, "Foreign key data type:", ct).ConfigureAwait(false);
        var remove = await console.ConfirmAsync("Remove this relation instead of adding?", defaultValue: false, ct).ConfigureAwait(false);

        await GenerateCommand.ExecuteRelationAsync(
            services,
            console,
            outputWriter,
            currentDirectoryProvider,
            parentEntity,
            childEntity,
            foreignKey,
            fkType,
            remove,
            solutionDir,
            dryRun: false,
            json: false,
            ct).ConfigureAwait(false);
    }

    private static async Task PromptValidateSolutionAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        CancellationToken ct)
    {
        var solutionDir = await PromptSolutionPathAsync(console, currentDirectoryProvider, ct).ConfigureAwait(false);
        await ValidateCommand.ExecuteAsync(
            services,
            console,
            outputWriter,
            currentDirectoryProvider,
            solutionDir,
            json: false,
            ct).ConfigureAwait(false);
    }

    private static async Task PromptWatchAsync(
        IServiceProvider services,
        IAnsiConsole console,
        TextWriter outputWriter,
        Func<string> currentDirectoryProvider,
        CancellationToken ct)
    {
        var solutionDir = await PromptSolutionPathAsync(console, currentDirectoryProvider, ct).ConfigureAwait(false);
        await WatchCommand.ExecuteAsync(
            services,
            console,
            outputWriter,
            currentDirectoryProvider,
            solutionDir,
            json: false,
            ct).ConfigureAwait(false);
    }

    private static async Task PromptLaunchUiAsync(
        IAnsiConsole console,
        TextWriter outputWriter,
        CancellationToken ct)
    {
        var port = await console.PromptAsync(
            new TextPrompt<int>("Port:")
                .DefaultValue(5180)
                .Validate(p => p is >= 1 and <= 65535
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Port must be between 1 and 65535.[/]")),
            ct).ConfigureAwait(false);

        await UiCommand.ExecuteAsync(console, outputWriter, port, json: false, headlessCheck: false, ct).ConfigureAwait(false);
    }

    private static Task<string> PromptSolutionPathAsync(
        IAnsiConsole console,
        Func<string> currentDirectoryProvider,
        CancellationToken ct) =>
        PromptDirectoryPathAsync(console, "Solution path", currentDirectoryProvider(), ct);

    private static async Task<string> PromptDirectoryPathAsync(
        IAnsiConsole console,
        string label,
        string defaultPath,
        CancellationToken ct)
    {
        var promptText = $"{Markup.Escape(label)} [green]({Markup.Escape(defaultPath)})[/]:";
        var entered = await console.PromptAsync(
            new TextPrompt<string>(promptText)
                .AllowEmpty(),
            ct).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(entered) ? defaultPath : entered.Trim();
    }

    private static Task<string> PromptIdentifierAsync(
        IAnsiConsole console,
        string prompt,
        CancellationToken ct) =>
        console.PromptAsync(
            new TextPrompt<string>(prompt)
                .Validate(id => NamingHelper.IsValidIdentifier(id)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be a valid non-keyword C# identifier.[/]")),
            ct);

    private static Task<string> PromptDataTypeAsync(
        IAnsiConsole console,
        string title,
        CancellationToken ct) =>
        console.PromptAsync(
            new SelectionPrompt<string>()
                .Title(Markup.Escape(title))
                .UseConverter(Markup.Escape)
                .AddChoices(DataTypes.All),
            ct);
}
