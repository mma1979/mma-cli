using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.AutoMapper
{
    public static class Config
    {
        public const string Template = "CreateMap<{{ EntityName }}, {{ EntityName }}ReadModel>().IgnoreAllPropertiesWithAnInaccessibleSetter().IgnoreAllSourcePropertiesWithAnInaccessibleSetter();\r\nCreateMap<{{ EntityName }}, {{ EntityName }}ModifyModel>().IgnoreAllPropertiesWithAnInaccessibleSetter().IgnoreAllSourcePropertiesWithAnInaccessibleSetter();";
    }
}