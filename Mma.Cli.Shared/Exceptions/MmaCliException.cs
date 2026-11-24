namespace Mma.Cli.Shared.Exceptions;

public sealed class MmaCliException : Exception
{
    public string Code { get; }
    public int ExitCode { get; }

    public MmaCliException(string code, string message, int exitCode = 1, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        ExitCode = exitCode;
    }
}
