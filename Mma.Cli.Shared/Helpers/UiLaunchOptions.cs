using Mma.Cli.Shared.Exceptions;

namespace Mma.Cli.Shared.Helpers;

public sealed record UiLaunchOptions(
    int Port = 5080,
    string? InitialPath = null,
    bool OpenBrowser = true,
    bool AllowLocalDb = true,
    bool ShowHelp = false)
{
    public static UiLaunchOptions Parse(string[] args)
    {
        var port = 5080;
        string? initialPath = null;
        var openBrowser = true;
        var allowLocalDb = true;
        var showHelp = false;

        // Skip leading command token ("ui") when present.
        var startIndex = args.Length > 0 && string.Equals(args[0], "ui", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;

                case "--no-browser":
                    openBrowser = false;
                    break;

                case "--no-local-db":
                    allowLocalDb = false;
                    break;

                case "-p":
                case "--port":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out var parsedPort) || parsedPort < 0 || parsedPort > 65535)
                    {
                        throw new MmaCliException(
                            "invalid_port",
                            "Invalid --port value. Provide 0 (auto-select) or a port number between 1 and 65535.",
                            exitCode: 2);
                    }
                    port = parsedPort;
                    break;

                case "--path":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        throw new MmaCliException(
                            "invalid_path",
                            "Option --path requires a directory path.",
                            exitCode: 2);
                    }
                    initialPath = Path.GetFullPath(args[++i]);
                    break;

                default:
                    throw new MmaCliException(
                        "invalid_option",
                        $"Unknown option '{arg}' for `mma ui`. Run `mma ui --help` for usage.",
                        exitCode: 2);
            }
        }

        return new UiLaunchOptions(port, initialPath, openBrowser, allowLocalDb, showHelp);
    }

    public static string HelpText => """
        USAGE:
           mma ui [options]

        DESCRIPTION:
           Launch the local MMA CLI Studio visual schema designer and code generator.

        OPTIONS:
           -p, --port <port>   Loopback port to listen on (default: 5080; use 0 for auto-pick)
           --path <dir>        Open or import the solution at <dir> automatically
           --no-browser        Do not automatically open the default browser
           --no-local-db       Disable loopback/private-network live DB introspection
           -h, --help          Show this help message

        EXAMPLES:
           mma ui
           mma ui --port 0 --no-browser
           mma ui --path ./Acme.Orders
        """;
}
