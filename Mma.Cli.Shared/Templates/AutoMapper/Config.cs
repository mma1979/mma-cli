using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.AutoMapper
{
    public static class Config
    {
        public const string Template = "CreateMap<$EntityName, $EntityNameReadModel>().IgnoreAllPropertiesWithAnInaccessibleSetter().IgnoreAllSourcePropertiesWithAnInaccessibleSetter();\r\nCreateMap<$EntityName, $EntityNameModifyModel>().IgnoreAllPropertiesWithAnInaccessibleSetter().IgnoreAllSourcePropertiesWithAnInaccessibleSetter();";
    }
}
