namespace Mma.Cli.Core.Config;

public interface IConfigStore
{
    string GetSetting(string solutionPath, string key, string defaultValue);
}
