﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.AutoMapper
{
    public static class DtoTemplate
    {
        public static string Template = @"using System;
using System.Collections.Generic;
using $SolutionName.Core.Database.Identity;

namespace $SolutionName.Core.Database.Tables
{
    public partial class $EntityNameDto
    {
        public $PK Id { get;  set; }
        public Guid? CreatedBy { get;  set; }
        public DateTime? CreatedDate { get;  set; }
        public Guid? ModifiedBy { get;  set; }
        public DateTime? ModifiedDate { get;  set; }
        public bool? IsDeleted { get;  set; }
        public Guid? DeletedBy { get;  set; }
        public DateTime? DeletedDate { get;  set; }
    }
}";
    }
}
