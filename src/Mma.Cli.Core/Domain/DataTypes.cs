namespace Mma.Cli.Core.Domain;

public static class DataTypes
{
    private static readonly string[] SupportedTypes =
    [
        "Guid",
        "int",
        "long",
        "string",
        "decimal",
        "float",
        "bool",
        "DateTime",
        "byte[]",
        "TimeSpan"
    ];

    private static readonly Dictionary<string, string> NormalizedMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["guid"] = "Guid",
        ["system.guid"] = "Guid",
        ["int"] = "int",
        ["int32"] = "int",
        ["system.int32"] = "int",
        ["long"] = "long",
        ["int64"] = "long",
        ["system.int64"] = "long",
        ["string"] = "string",
        ["system.string"] = "string",
        ["decimal"] = "decimal",
        ["system.decimal"] = "decimal",
        ["float"] = "float",
        ["single"] = "float",
        ["system.single"] = "float",
        ["bool"] = "bool",
        ["boolean"] = "bool",
        ["system.boolean"] = "bool",
        ["datetime"] = "DateTime",
        ["system.datetime"] = "DateTime",
        ["byte[]"] = "byte[]",
        ["timespan"] = "TimeSpan",
        ["system.timespan"] = "TimeSpan"
    };

    public static IReadOnlyList<string> All => SupportedTypes;

    public static bool IsSupported(string? type) => Normalize(type) is not null;

    public static string? Normalize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        return NormalizedMap.TryGetValue(type.Trim(), out var canonical) ? canonical : null;
    }
}
