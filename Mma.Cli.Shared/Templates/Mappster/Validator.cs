using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates
{
    public static class MapsterValidator
    {
        public const string Template = @"using FluentValidation;

using $SolutionName.Core.Database.Tables;

namespace $SolutionName.Core.Validations
{
    public class $EntityNameValidator:AbstractValidator<$EntityNameDto>
    {

        public $EntityNameValidator()
        {
           
        }


    }
}";
    }
}
