using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.Core.CodeGen;
using Mma.Cli.Core.Config;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Export;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Process;
using Mma.Cli.Core.Services;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Core.Templates;

namespace Mma.Cli.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMmaCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(SolutionExporter.HttpClientName);
        services.AddHttpClient(SolutionExporter.InsecureLocalhostHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() =>
                SolutionExporter.CreateHttpMessageHandler("https://localhost", allowInsecureLocalhostTls: true));

        services.AddSingleton<ISolutionLocator, SolutionLocator>();
        services.AddSingleton<IConfigStore, JsonConfigStore>();
        services.AddSingleton<ITemplateProvider, EmbeddedTemplateProvider>();
        services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();
        services.AddSingleton<IDryRunDiffEngine, DryRunDiffEngine>();
        services.AddSingleton<IDotnetRunner, DotnetRunner>();
        services.AddSingleton<ISolutionLockManager, SolutionLockManager>();
        services.AddSingleton<IManifestStore, ManifestStore>();
        services.AddTransient<IProjectValidator, ProjectValidator>();
        services.AddTransient<ISolutionExporter, SolutionExporter>();
        services.AddTransient<ISolutionService, SolutionService>();
        services.AddTransient<IEntityService, EntityService>();
        services.AddTransient<IPropertyService, PropertyService>();
        services.AddTransient<IRelationService, RelationService>();

        return services;
    }
}
