using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.Mappster
{
    public static class Entity
    {
        public static string Template = @"using $SolutionName.Common;
using $SolutionName.Core.Validations;

using FluentValidation.Results;

using Mapster;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;

namespace $SolutionName.Core.Database.Tables
{
    [AdaptTo(""[name]ReadModel"")]
    [AdaptTo(""[name]ModifyModel"")]
    [GenerateMapper]
    public class $EntityName : BaseEntity<$PK>
    {
        


        $EntityNameValidator _Validator;
        private $EntityNameValidator Validator
        {
            get
            {
                _Validator ??= new $EntityNameValidator();
                return _Validator;
            }
        }


        private $EntityName()
        {
            
        }

        

        public $EntityName($EntityNameModifyModel model)
        {
            ValidationResult result = Validator.Validate(model);
            if (!result.IsValid)
            {
                var messages = result.Errors.Select(e => e.ErrorMessage);
                throw new HttpException(LoggingEvents.Constractor_ERROR, JsonConvert.SerializeObject(messages));
            }

            
            CreatedDate = DateTime.UtcNow;

        }

        public $EntityName Update($EntityNameModifyModel model)
        {
            ValidationResult result = Validator.Validate(model);
            if (!result.IsValid)
            {
                var messages = result.Errors.Select(e => e.ErrorMessage);
                throw new HttpException(LoggingEvents.Constractor_ERROR, JsonConvert.SerializeObject(messages));
            }

           

            ModifiedDate = DateTime.UtcNow;
            return this;
        }

        public $EntityName Delete()
        {
            IsDeleted = true;
            DeletedDate = DateTime.UtcNow;
            return this;
        }


    }
}";
    }
}
