using Spectre.Console;

namespace Mma.Cli.App;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            if (!cts.IsCancellationRequested)
            {
                e.Cancel = true;
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            await using var services = CliApp.BuildServiceProvider();
            return await CliApp.RunAsync(
                args,
                services,
                AnsiConsole.Console,
                Console.Out,
                Console.Error,
                Directory.GetCurrentDirectory,
                ct: cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }
}
