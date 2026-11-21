namespace Mma.Cli.Bff.Security;

public sealed class BffSecurityOptions
{
    public const string SectionName = "BffSecurity";

    public string? BootstrapToken { get; set; }
    public bool AllowDevAutoSession { get; set; }
    public List<string> AllowedRoots { get; set; } = [];
    public string CookieName { get; set; } = "mma_bff_session";
    public string CsrfHeaderName { get; set; } = "X-Mma-Csrf";

    public static BffSecurityOptions LoadFromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new BffSecurityOptions();
        var section = configuration.GetSection(SectionName);

        if (section.GetSection(nameof(BootstrapToken)).Exists())
        {
            options.BootstrapToken = section[nameof(BootstrapToken)];
        }
        else
        {
            options.BootstrapToken = configuration["MMA_BFF_BOOTSTRAP_TOKEN"]
                ?? Environment.GetEnvironmentVariable("MMA_BFF_BOOTSTRAP_TOKEN");
        }

        if (section.GetSection(nameof(AllowDevAutoSession)).Exists()
            && bool.TryParse(section[nameof(AllowDevAutoSession)], out var configuredDevAuth))
        {
            options.AllowDevAutoSession = configuredDevAuth;
        }
        else
        {
            var rawEnvDev = configuration["MMA_BFF_DEV_AUTH"]
                ?? Environment.GetEnvironmentVariable("MMA_BFF_DEV_AUTH");
            options.AllowDevAutoSession = string.Equals(rawEnvDev, "1", StringComparison.Ordinal)
                || string.Equals(rawEnvDev, "true", StringComparison.OrdinalIgnoreCase);
        }

        var rootsSection = section.GetSection(nameof(AllowedRoots));
        if (rootsSection.Exists())
        {
            foreach (var child in rootsSection.GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(child.Value))
                {
                    options.AllowedRoots.Add(child.Value.Trim());
                }
            }
        }

        if (options.AllowedRoots.Count == 0)
        {
            var rawRoots = configuration["MMA_BFF_ALLOWED_ROOTS"]
                ?? Environment.GetEnvironmentVariable("MMA_BFF_ALLOWED_ROOTS");
            if (!string.IsNullOrWhiteSpace(rawRoots))
            {
                foreach (var segment in rawRoots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        options.AllowedRoots.Add(segment);
                    }
                }
            }
        }

        if (options.AllowedRoots.Count == 0)
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            options.AllowedRoots.Add(string.IsNullOrWhiteSpace(userHome) ? "." : userHome);
        }

        var cookieName = section[nameof(CookieName)];
        if (!string.IsNullOrWhiteSpace(cookieName))
        {
            options.CookieName = cookieName.Trim();
        }

        var csrfHeader = section[nameof(CsrfHeaderName)];
        if (!string.IsNullOrWhiteSpace(csrfHeader))
        {
            options.CsrfHeaderName = csrfHeader.Trim();
        }

        return options;
    }
}
