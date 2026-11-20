using System.Text.Json;
using System.Text.Json.Serialization;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Spectre.Console;

namespace Mma.Cli.App.Output;

public sealed record CliJsonError(string Code, string Message, string? Details = null);

public sealed record CliJsonEnvelope(bool Success, string Command, object? Data = null, CliJsonError? Error = null);

public sealed record DoctorDotnetCheck(bool Available, string? Version, string Status, string Message);

public sealed record DoctorSolutionCheck(
    bool Found,
    string? SolutionName,
    string? SolutionPath,
    bool? IsValid,
    int IssueCount,
    string Status,
    string Message);

public sealed record DoctorReport(
    DoctorDotnetCheck DotnetSdk,
    DoctorDotnetCheck DotnetEf,
    string WorkspaceDbPath,
    bool WorkspaceDbAccessible,
    string WorkspaceDbStatus,
    DoctorSolutionCheck Solution);

public static class CliOutputFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static void WriteJson(TextWriter outputWriter, CliJsonEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(outputWriter);
        ArgumentNullException.ThrowIfNull(envelope);

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        outputWriter.WriteLine(json);
        outputWriter.Flush();
    }

    public static void WriteSuccess(
        IAnsiConsole console,
        TextWriter outputWriter,
        bool json,
        string command,
        object data,
        string message,
        GenerationSummary? summary = null,
        IReadOnlyList<string>? warnings = null)
    {
        if (json)
        {
            WriteJson(outputWriter, new CliJsonEnvelope(true, command, data));
            return;
        }

        console.MarkupLine($"[green]{Markup.Escape(message)}[/]");

        if (summary is not null)
        {
            if (summary.DryRun)
            {
                console.MarkupLine("[yellow]Dry run mode: no files were written to disk.[/]");
            }

            console.MarkupLine(
                $"[grey]Created: {summary.CreatedFiles.Count}, Modified: {summary.ModifiedFiles.Count}, Deleted: {summary.DeletedFiles.Count}[/]");
        }

        if (warnings is { Count: > 0 })
        {
            foreach (var warning in warnings)
            {
                console.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
            }
        }
    }

    public static void WriteFailure(
        IAnsiConsole console,
        TextWriter outputWriter,
        bool json,
        string command,
        GenerationError? error,
        string fallbackMessage,
        object? data = null) =>
        WriteFailure(
            console,
            outputWriter,
            json,
            command,
            error?.Code ?? ErrorCodes.VALIDATION_ERROR,
            error?.Message ?? fallbackMessage,
            error?.Details,
            data);

    public static void WriteFailure(
        IAnsiConsole console,
        TextWriter outputWriter,
        bool json,
        string command,
        string code,
        string message,
        string? details = null,
        object? data = null)
    {
        if (json)
        {
            WriteJson(outputWriter, new CliJsonEnvelope(false, command, data, new CliJsonError(code, message, details)));
            return;
        }

        console.MarkupLine($"[red]Error ({Markup.Escape(code)}):[/] {Markup.Escape(message)}");
        if (!string.IsNullOrWhiteSpace(details))
        {
            console.MarkupLine($"[grey]{Markup.Escape(details)}[/]");
        }
    }

    public static void WriteValidationReport(
        IAnsiConsole console,
        TextWriter outputWriter,
        bool json,
        ProjectValidationReport report)
    {
        if (json)
        {
            if (report.IsValid)
            {
                WriteJson(outputWriter, new CliJsonEnvelope(true, "validate", report));
            }
            else
            {
                var errorCount = report.Issues.Count(i =>
                    string.Equals(i.Severity, "Error", StringComparison.OrdinalIgnoreCase));
                WriteJson(outputWriter, new CliJsonEnvelope(
                    false,
                    "validate",
                    report,
                    new CliJsonError(
                        ErrorCodes.VALIDATION_ERROR,
                        $"Solution '{report.SolutionName}' failed validation with {errorCount} error(s).")));
            }

            return;
        }

        if (report.Issues.Count > 0)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Severity")
                .AddColumn("Code")
                .AddColumn("Target")
                .AddColumn("Message");

            foreach (var issue in report.Issues)
            {
                var severityMarkup = string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase)
                    ? $"[red]{Markup.Escape(issue.Severity)}[/]"
                    : $"[yellow]{Markup.Escape(issue.Severity)}[/]";

                table.AddRow(
                    severityMarkup,
                    Markup.Escape(issue.Code),
                    Markup.Escape(issue.Target ?? "-"),
                    Markup.Escape(issue.Message));
            }

            console.Write(table);
        }

        if (report.IsValid)
        {
            console.MarkupLine($"[green]Solution '{Markup.Escape(report.SolutionName)}' is valid.[/]");
        }
        else
        {
            console.MarkupLine($"[red]Solution '{Markup.Escape(report.SolutionName)}' failed validation.[/]");
        }
    }

    public static void WriteDoctorReport(
        IAnsiConsole console,
        TextWriter outputWriter,
        bool json,
        DoctorReport report,
        bool passed)
    {
        if (json)
        {
            if (passed)
            {
                WriteJson(outputWriter, new CliJsonEnvelope(true, "doctor", report));
            }
            else
            {
                WriteJson(outputWriter, new CliJsonEnvelope(
                    false,
                    "doctor",
                    report,
                    new CliJsonError(ErrorCodes.VALIDATION_ERROR, "One or more critical diagnostic checks failed.")));
            }

            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Check")
            .AddColumn("Status")
            .AddColumn("Details");

        table.AddRow(
            ".NET SDK",
            FormatStatusMarkup(report.DotnetSdk.Status),
            Markup.Escape(report.DotnetSdk.Version ?? report.DotnetSdk.Message));

        table.AddRow(
            "dotnet-ef",
            FormatStatusMarkup(report.DotnetEf.Status),
            Markup.Escape(report.DotnetEf.Version ?? report.DotnetEf.Message));

        table.AddRow(
            "Workspace DB",
            FormatStatusMarkup(report.WorkspaceDbStatus),
            Markup.Escape(report.WorkspaceDbPath));

        var solutionDetail = report.Solution.Found
            ? $"{report.Solution.SolutionName} ({report.Solution.Message})"
            : report.Solution.Message;

        table.AddRow(
            "Solution",
            FormatStatusMarkup(report.Solution.Status),
            Markup.Escape(solutionDetail));

        console.Write(table);
    }

    public static IProgress<GenerationEvent>? CreateProgress(IAnsiConsole console, bool json) =>
        json ? null : new InlineConsoleProgress(console);

    private static string FormatStatusMarkup(string status) => status.ToUpperInvariant() switch
    {
        "OK" => $"[green]{Markup.Escape(status)}[/]",
        "WARNING" => $"[yellow]{Markup.Escape(status)}[/]",
        "ERROR" => $"[red]{Markup.Escape(status)}[/]",
        _ => $"[blue]{Markup.Escape(status)}[/]"
    };

    private sealed class InlineConsoleProgress(IAnsiConsole console) : IProgress<GenerationEvent>
    {
        public void Report(GenerationEvent value)
        {
            if (value.Stage is GenerationStage.Scaffolding or GenerationStage.Updating)
            {
                console.MarkupLine($"[grey]{Markup.Escape(value.Message)}[/]");
            }
        }
    }
}
