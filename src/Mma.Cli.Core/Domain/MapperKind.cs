namespace Mma.Cli.Core.Domain;

public enum MapperKind
{
    AutoMapper,
    Mapster
}

public static class MapperKindExtensions
{
    public static string ToFolderName(this MapperKind mapper) => mapper switch
    {
        MapperKind.Mapster => "Mapster",
        _ => "AutoMapper"
    };
}

public static class MapperKindParser
{
    public static bool TryParse(string? value, out MapperKind mapper)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mapper = MapperKind.AutoMapper;
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "automapper":
                mapper = MapperKind.AutoMapper;
                return true;
            case "mapster":
                mapper = MapperKind.Mapster;
                return true;
            default:
                mapper = MapperKind.AutoMapper;
                return false;
        }
    }
}
