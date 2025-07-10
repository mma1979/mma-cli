using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.Mappster
{
    public static class Entity
    {
        public static string Template = @"using {{ SolutionName }}.Common;
using {{ SolutionName }}.Core.Validations;

using FluentValidation.Results;

using Mapster;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;

namespace {{ SolutionName }}.Core.Database.Tables
{
    [AdaptTo(""[name]ReadModel"")]
    [AdaptTo(""[name]ModifyModel"")]
    [GenerateMapper]
    public class {{ EntityName }} : BaseEntity<{{ PK }}>
    {
        


        {{ EntityName }}Validator _Validator;
        private {{ EntityName }}Validator Validator
        {
            get
            {
                _Validator ??= new {{ EntityName }}Validator();
                return _Validator;
            }
        }


        private {{ EntityName }}()
        {
            
        }

        

        public {{ EntityName }}({{ EntityName }}ModifyModel model)
        {
            ValidationResult result = Validator.Validate(model);
            if (!result.IsValid)
            {
                var messages = result.Errors.Select(e => e.ErrorMessage);
                throw new HttpException(LoggingEvents.Constractor_ERROR, JsonConvert.SerializeObject(messages));
            }

            
            CreatedDate = DateTime.UtcNow;

        }

        public {{ EntityName }} Update({{ EntityName }}ModifyModel model)
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

        public {{ EntityName }} Delete()
        {
            IsDeleted = true;
            DeletedDate = DateTime.UtcNow;
            return this;
        }


    }
}";
    }
}