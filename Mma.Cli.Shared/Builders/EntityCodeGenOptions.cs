using Mma.Cli.Shared.Consts;

namespace Mma.Cli.Shared.Builders;

public sealed record EntityCodeGenOptions(
    bool GenerateService = true,
    bool GenerateApiController = true,
    bool GetAll = true,
    bool Select = true,
    bool Find = true,
    bool Add = true,
    bool Update = true,
    bool Delete = true)
{
    public const string MethodGetAll = "getAll";
    public const string MethodSelect = "select";
    public const string MethodFind = "find";
    public const string MethodAdd = "add";
    public const string MethodUpdate = "update";
    public const string MethodDelete = "delete";

    public static readonly string[] AllMethodNames =
    [
        MethodGetAll,
        MethodSelect,
        MethodFind,
        MethodAdd,
        MethodUpdate,
        MethodDelete,
    ];

    public static EntityCodeGenOptions Default { get; } = new();

    public bool EffectiveGenerateApiController => GenerateService && GenerateApiController;

    public bool AllMethodsEnabled => GetAll && Select && Find && Add && Update && Delete;

    public IReadOnlyList<string> GetEnabledMethodNames()
    {
        var enabled = new List<string>(6);
        if (GetAll) enabled.Add(MethodGetAll);
        if (Select) enabled.Add(MethodSelect);
        if (Find) enabled.Add(MethodFind);
        if (Add) enabled.Add(MethodAdd);
        if (Update) enabled.Add(MethodUpdate);
        if (Delete) enabled.Add(MethodDelete);
        return enabled;
    }

    public string? ToServiceMethodsCsv()
    {
        if (AllMethodsEnabled)
        {
            return null;
        }

        var enabled = GetEnabledMethodNames();
        return enabled.Count == 0 ? "none" : string.Join(",", enabled);
    }

    public Dictionary<string, bool> ToServiceMethodsDictionary() => new(StringComparer.OrdinalIgnoreCase)
    {
        [MethodGetAll] = GetAll,
        [MethodSelect] = Select,
        [MethodFind] = Find,
        [MethodAdd] = Add,
        [MethodUpdate] = Update,
        [MethodDelete] = Delete,
    };

    public IReadOnlyList<string> ToCliFlags()
    {
        var flags = new List<string>();
        if (!GenerateService)
        {
            flags.Add(Flags.ServiceFlag);
            return flags;
        }

        if (!GenerateApiController)
        {
            flags.Add(Flags.ApiFlag);
        }

        if (!AllMethodsEnabled)
        {
            flags.Add(Flags.MethodsFlag);
            flags.Add(ToServiceMethodsCsv() ?? "none");
        }

        return flags;
    }

    public static EntityCodeGenOptions FromServiceMethodsCsv(
        bool generateService,
        bool generateApiController,
        string? csv)
    {
        var effectiveApi = generateService && generateApiController;
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new EntityCodeGenOptions(generateService, effectiveApi);
        }

        var trimmed = csv.Trim();
        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase))
        {
            return new EntityCodeGenOptions(
                GenerateService: generateService,
                GenerateApiController: effectiveApi,
                GetAll: false,
                Select: false,
                Find: false,
                Add: false,
                Update: false,
                Delete: false);
        }

        var tokens = trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new EntityCodeGenOptions(
            GenerateService: generateService,
            GenerateApiController: effectiveApi,
            GetAll: tokens.Contains(MethodGetAll) || tokens.Contains("all"),
            Select: tokens.Contains(MethodSelect),
            Find: tokens.Contains(MethodFind),
            Add: tokens.Contains(MethodAdd),
            Update: tokens.Contains(MethodUpdate),
            Delete: tokens.Contains(MethodDelete));
    }

    public static EntityCodeGenOptions FromDictionary(
        bool generateService,
        bool generateApiController,
        IReadOnlyDictionary<string, bool>? methods)
    {
        var effectiveApi = generateService && generateApiController;
        if (methods is null)
        {
            return new EntityCodeGenOptions(generateService, effectiveApi);
        }

        static bool ReadFlag(IReadOnlyDictionary<string, bool> dict, string key)
        {
            foreach (var kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return true;
        }

        return new EntityCodeGenOptions(
            GenerateService: generateService,
            GenerateApiController: effectiveApi,
            GetAll: ReadFlag(methods, MethodGetAll),
            Select: ReadFlag(methods, MethodSelect),
            Find: ReadFlag(methods, MethodFind),
            Add: ReadFlag(methods, MethodAdd),
            Update: ReadFlag(methods, MethodUpdate),
            Delete: ReadFlag(methods, MethodDelete));
    }

    public static EntityCodeGenOptions FromArgs(string[] args)
    {
        var generateService = !args.Any(a => string.Equals(a, Flags.ServiceFlag, StringComparison.OrdinalIgnoreCase));
        var generateApi = generateService && !args.Any(a => string.Equals(a, Flags.ApiFlag, StringComparison.OrdinalIgnoreCase));

        string? methodsCsv = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], Flags.MethodsFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                methodsCsv = args[i + 1];
                break;
            }
        }

        return FromServiceMethodsCsv(generateService, generateApi, methodsCsv);
    }
}
