using Mma.Cli.Shared.Builders;

namespace Mma.Cli.Shared.Templates.Mappster
{
    public static class MappsterController
    {
        public static string Template => Render(EntityCodeGenOptions.Default);

        public static string Render(EntityCodeGenOptions options) =>
            AutoMapper.Controller.Render(options);
    }
}
