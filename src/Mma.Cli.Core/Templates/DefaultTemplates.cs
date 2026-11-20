using Mma.Cli.Core.Domain;

namespace Mma.Cli.Core.Templates;

internal static class DefaultTemplates
{
    public const string DbSetEntry =
        "            public virtual DbSet<$EntityName> $EntitySetName { get; set; }";

    public const string ConfigEntry =
        "            modelBuilder.ApplyConfiguration(new $EntityNameConfig());";

    public const string AutoMapperConfig =
        "CreateMap<$EntityName, $EntityNameReadModel>().IgnoreAllPropertiesWithAnInaccessibleSetter().IgnoreAllSourcePropertiesWithAnInaccessibleSetter();\r\nCreateMap<$EntityName, $EntityNameModifyModel>().IgnoreAllPropertiesWithAnInaccessibleSetter().IgnoreAllSourcePropertiesWithAnInaccessibleSetter();";

    public const string Validator = """
using FluentValidation;

using $SolutionName.Core.Database.Tables;

namespace $SolutionName.Core.Validations
{
    public class $EntityNameValidator:AbstractValidator<$EntityNameModifyModel>
    {

        public $EntityNameValidator()
        {
           
        }


    }
}
""";

    public const string AutoMapperModifyModel = """
using System;
using System.Collections.Generic;
using $SolutionName.Core.Database.Identity;

namespace $SolutionName.Core.Database.Tables
{
    public partial class $EntityNameModifyModel
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
}
""";

    public const string AutoMapperReadModel = """
using System;
using $SolutionName.Core.Database.Identity;

namespace $SolutionName.Core.Database.Tables
{
    public partial class $EntityNameReadModel
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
}
""";

    public const string MapsterModifyModel = """
using System;
using System.Collections.Generic;
using $SolutionName.Core.Database.Identity;

namespace $SolutionName.Core.Database.Tables
{
    public partial class $EntityNameDto
    {
        public $PK Id { get;  set; }
        public long? CreatedBy { get;  set; }
        public DateTime? CreatedDate { get;  set; }
        public long? ModifiedBy { get;  set; }
        public DateTime? ModifiedDate { get;  set; }
        public bool? IsDeleted { get;  set; }
        public long? DeletedBy { get;  set; }
        public DateTime? DeletedDate { get;  set; }
    }
}
""";

    public const string MapsterReadModel = MapsterModifyModel;

    public const string AutoMapperEntity = """
using $SolutionName.Common;
using $SolutionName.Core.Validations;

using FluentValidation.Results;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;

namespace $SolutionName.Core.Database.Tables
{
    
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
}
""";

    public const string MapsterEntity = """
using $SolutionName.Common;
using $SolutionName.Core.Validations;

using FluentValidation.Results;

using Mapster;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;

namespace $SolutionName.Core.Database.Tables
{
    [AdaptTo("[name]ReadModel")]
    [AdaptTo("[name]ModifyModel")]
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
}
""";

    public const string EntityConfig = """
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using $SolutionName.Core.Database.Identity;
using $SolutionName.Core.Database.Tables;

using $SolutionName.EntityFramework.Infrastrcture.ValueGenerator;

namespace $SolutionName.EntityFramework.EntityConfigurations
{
	public class $EntityNameConfig : IEntityTypeConfiguration<$EntityName>
	{
		private readonly string _schema;
		public $EntityNameConfig(string schema = "dbo")
		{
			_schema = schema;
		}

	   
		public void Configure(EntityTypeBuilder<$EntityName> builder)
		{
			builder.ToTable("$EntitySetName", _schema);
			
			/* // Uncoment if the PK is Guid
			 builder.Property(e => e.Id)
			  .ValueGeneratedOnAdd()
			  .HasValueGenerator<GuidV7ValueGenerator>();
			*/

			 builder.HasQueryFilter(e => e.IsDeleted != true);
			builder.Property(e => e.IsDeleted).IsRequired()
				.HasDefaultValueSql("((0))");


			builder.Property(e => e.CreatedDate)
			 .HasColumnType("datetime")
			 .ValueGeneratedOnAdd()
			 .HasValueGenerator<CreatedDateTimeValueGenerator>();

			builder.Property(e => e.ModifiedDate)
				.HasColumnType("datetime");

			builder.HasIndex(e => e.IsDeleted);
			builder.Property(e => e.DeletedDate)
				.HasColumnType("datetime");


		}
	}
}
""";

    public const string AutoMapperService = """
using AutoMapper;

using $SolutionName.Core.Database.Tables;
using $SolutionName.Core.Models;
using $SolutionName.EntityFramework;
using $SolutionName.Services.Chache;
using $SolutionName.Core.Consts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace $SolutionName.Services
{


    public class $EntityNameService : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<$EntityNameService> _logger;
        private readonly ICacheService _cacheService;
        private readonly IMapper _mapper;

        private readonly string CACHING_PREFIX = "$EntityName:";

        public $EntityNameService(ApplicationDbContext context, ILogger<$EntityNameService> logger, ICacheService cacheService, IMapper mapper)
        {
            _context = context;
            _logger = logger;
            _cacheService = cacheService;
            _mapper = mapper;
        }



        public async Task<ResultViewModel<List<$EntityNameReadModel>>> All(QueryViewModel query)
        {
			var cacheKey = $"{CACHING_PREFIX}{query.GetHashCode()}";
            try
            {
                var cached = _cacheService.Get<ResultViewModel<List<$EntityNameReadModel>>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }


                var data = query.ShowAll ?
                        _context.$EntitySetName.IgnoreQueryFilters().AsQueryable() :
                        _context.$EntitySetName.AsQueryable();
                if (!string.IsNullOrEmpty(query.Filter))
                {
                    data = data.Where(query.Filter);
                }

                query.Order = string.IsNullOrEmpty(query.Order) ? "CreatedDate Desc" : query.Order;
                data = data.OrderBy(query.Order);

                var page = query.PageNumber <= 0 ? data :
                           data.Skip((query.PageNumber - 1) * query.PageSize)
                           .Take(query.PageSize);

                var count = await data.CountAsync();
                var list = await page.ToListAsync();

                var result = new ResultViewModel<List<$EntityNameReadModel>>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
                    Total = count,
                    PageSize = query.PageSize,
                    PageNumber = query.PageNumber,
                    Filter = query.Filter,
                    Data = _mapper.Map<List<$EntityNameReadModel>>(list)
                };

                _cacheService.Set(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_READ_ERROR },
                    Filter = query.Filter,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize,
                };
            }
        }

        public async Task<ResultViewModel<List<dynamic>>> Select(QueryViewModel query)
		{
			var cacheKey = $"{CACHING_PREFIX}-select{query.GetHashCode()}";
			try
			{
				var cached = _cacheService.Get<ResultViewModel<List<dynamic>>>(cacheKey);

				if (cached != null)
				{
					return cached;
				}


				var data = query.ShowAll ?
						_context.$EntitySetName.IgnoreQueryFilters().AsQueryable() :
						_context.$EntitySetName.AsQueryable();
				if (!string.IsNullOrEmpty(query.Filter))
				{
					data = data.Where(query.Filter);
				}

				query.Order = string.IsNullOrEmpty(query.Order) ? "CreatedDate Desc" : query.Order;
				data = data.OrderBy(query.Order);

				var page = query.PageNumber <= 0 ? data :
						   data.Skip((query.PageNumber - 1) * query.PageSize)
						   .Take(query.PageSize);

				var count = await data.CountAsync();
				var list = await page.Select($"new({query.Fields}").ToDynamicListAsync();

				var result = new ResultViewModel<List<dynamic>>
				{
					IsSuccess = true,
					StatusCode = 200,
					Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
					Total = count,
					PageSize = query.PageSize,
					PageNumber = query.PageNumber,
					Filter = query.Filter,
					Data = list
				};

				_cacheService.Set(cacheKey, result);

				return result;
			}
			catch (Exception ex)
			{

				_logger.LogError(ex.Message, ex);
				return new()
				{
					IsSuccess = false,
					StatusCode = 500,
					Messages = { ResourcesKeys.DATA_READ_ERROR },
					Filter = query.Filter,
					PageNumber = query.PageNumber,
					PageSize = query.PageSize,
				};
			}
		}

        public async Task<ResultViewModel<$EntityNameModifyModel>> Find(Expression<Func<$EntityName, bool>> predicate)
        {
			var cacheKey = $"{CACHING_PREFIX}{predicate.Body.GetHashCode()}";
            try
            {
                var cached = _cacheService.Get<ResultViewModel<$EntityNameModifyModel>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }

                var data = await _context.$EntitySetName.SingleOrDefaultAsync(predicate);
                var result = new ResultViewModel<$EntityNameModifyModel>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
                    Data = _mapper.Map<$EntityNameModifyModel>(data)
                };

                _cacheService.Set(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_LOAD_ERROR },
                };
            }
        }
        public async Task<ResultViewModel<$EntityNameModifyModel>> Find($PK id)
        {
			var cacheKey = $"{CACHING_PREFIX}{id.GetHashCode()}";
            try
            {

                var cached = _cacheService.Get<ResultViewModel<$EntityNameModifyModel>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }

                var data = await _context.$EntitySetName.FindAsync(id);
                var result = new ResultViewModel<$EntityNameModifyModel>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = {  ResourcesKeys.DATA_LOAD_SUCCESS },
                    Data = _mapper.Map<$EntityNameModifyModel>(data)
                };

                _cacheService.Set(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_LOAD_ERROR },
                };
            }
        }


        public async Task<AcknowledgeViewModel> Add($EntityNameModifyModel model)
        {
            try
            {
                var record = new $EntityName(model);
                var entity = await _context.$EntitySetName.AddAsync(record);
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear("{CACHING_PREFIX}*");
                return new ()
                {

                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_SAVE_SUCCESS },
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_SAVE_ERROR },
                };
            }
        }

        public async Task<AcknowledgeViewModel> Update($EntityNameModifyModel model)
        {
            try
            {
                var record = await _context.$EntitySetName.FindAsync(model.Id);
                if (record == null)
                {
                    var exp = new KeyNotFoundException($"item number {model.Id} does not Exist");
                    _logger.LogError(exp.Message, exp);
                    return new ()
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ResourcesKeys.ITEM_NOT_FOUND },
                    };
                }


                var entity = record.Update(model);

                _ = await _context.SaveChangesAsync();

                _cacheService.Clear("{CACHING_PREFIX}*");

                return new ()
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_MODIFY_SUCCESS }
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_SAVE_ERROR },
                };
            }
        }

        public async Task<AcknowledgeViewModel> Delete($PK id)
        {
            try
            {
                var record = await _context.$EntitySetName.FindAsync(id);
                if (record == null)
                {
                    var exp = new KeyNotFoundException($"item number {id} does not Exist");
                    _logger.LogError(exp.Message, exp);
                    return new ()
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ResourcesKeys.ITEM_NOT_FOUND },
                    };
                }
                var entity = record.Delete();
                _context.Entry(entity).State = EntityState.Deleted;
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear("{CACHING_PREFIX}*");

                return new ()
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_REMOVE_SUCCESS }
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_REMOVE_ERROR },
                };
            }
        }

#region IDisposable Support
        public void Dispose(bool dispose)
        {
            if (dispose)
            {
                Dispose();

            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            GC.Collect();
        }





        #endregion

    }
}
""";

    public const string MapsterService = """
using $SolutionName.Core.Database.Tables;
using $SolutionName.Core.Models;
using $SolutionName.EntityFramework;
using $SolutionName.Services.Chache;
using $SolutionName.Core.Consts;

using Mapster;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace $SolutionName.Services
{


    public class $EntityNameService
    {
		private readonly ApplicationDbContext _context;
        private readonly ILogger<$EntityNameService> _logger;
        private readonly ICacheService _cacheService;

        private readonly string CACHING_PREFIX = "$EntityName:";

        public $EntityNameService(ApplicationDbContext context, ILogger<$EntityNameService> logger, ICacheService cacheService)
        {
            _context = context;
            _logger = logger;
            _cacheService = cacheService;
        }



        public async Task<ResultViewModel<List<$EntityNameReadModel>>> All(QueryViewModel query)
        {
			var cacheKey = $"{CACHING_PREFIX}{query.GetHashCode()}";
            try
            {
                var cached = _cacheService.Get<ResultViewModel<List<$EntityNameReadModel>>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }


                var data = query.ShowAll ?
                        _context.$EntitySetName.IgnoreQueryFilters().AsQueryable() :
                        _context.$EntitySetName.AsQueryable();
                if (!string.IsNullOrEmpty(query.Filter))
                {
                    data = data.Where(query.Filter);
                }

                query.Order = string.IsNullOrEmpty(query.Order) ? "Id Desc" : query.Order;
                data = data.OrderBy(query.Order);

                var page = query.PageNumber <= 0 ? data :
                           data.Skip((query.PageNumber - 1) * query.PageSize)
                           .Take(query.PageSize);

                var count = await data.CountAsync();
                var list = await page.ToListAsync();

                var result = new ResultViewModel<List<$EntityNameReadModel>>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
                    Total = count,
                    PageSize = query.PageSize,
                    PageNumber = query.PageNumber,
                    Filter = query.Filter,
                    Data = list.Adapt<List<$EntityNameReadModel>>()
                };

                _cacheService.Set(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<List<$EntityNameReadModel>>
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_READ_ERROR },
                    Filter = query.Filter,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize,
                };
            }
        }

        public async Task<ResultViewModel<List<dynamic>>> Select(QueryViewModel query)
{
	var cacheKey = $"{CACHING_PREFIX}-select{query.GetHashCode()}";
	try
	{
		var cached = _cacheService.Get<ResultViewModel<List<dynamic>>>(cacheKey);

		if (cached != null)
		{
			return cached;
		}


		var data = query.ShowAll ?
				_context.$EntitySetName.IgnoreQueryFilters().AsQueryable() :
				_context.$EntitySetName.AsQueryable();
		if (!string.IsNullOrEmpty(query.Filter))
		{
			data = data.Where(query.Filter);
		}

		query.Order = string.IsNullOrEmpty(query.Order) ? "CreatedDate Desc" : query.Order;
		data = data.OrderBy(query.Order);

		var page = query.PageNumber <= 0 ? data :
				   data.Skip((query.PageNumber - 1) * query.PageSize)
				   .Take(query.PageSize);

		var count = await data.CountAsync();
		var list = await page.Select($"new({query.Fields}").ToDynamicListAsync();

		var result = new ResultViewModel<List<dynamic>>
		{
			IsSuccess = true,
			StatusCode = 200,
			Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
			Total = count,
			PageSize = query.PageSize,
			PageNumber = query.PageNumber,
			Filter = query.Filter,
			Data = list
		};

		_cacheService.Set(cacheKey, result);

		return result;
	}
	catch (Exception ex)
	{

		_logger.LogError(ex.Message, ex);
		return new()
		{
			IsSuccess = false,
			StatusCode = 500,
			Messages = { ResourcesKeys.DATA_READ_ERROR },
			Filter = query.Filter,
			PageNumber = query.PageNumber,
			PageSize = query.PageSize,
		};
	}
}

        public async Task<ResultViewModel<$EntityNameModifyModel>> Find(Expression<Func<$EntityName, bool>> predicate)
        {
			var cacheKey = $"{CACHING_PREFIX}{predicate.Body.GetHashCode()}";
            try
            {
                var cached = _cacheService.Get<ResultViewModel<$EntityNameModifyModel>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }

                var data = await _context.$EntitySetName.SingleOrDefaultAsync(predicate);
                var result = new ResultViewModel<$EntityNameModifyModel>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
                    Data = data.Adapt<$EntityNameModifyModel>()
                };

                _cacheService.Set(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<$EntityNameModifyModel>
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_LOAD_ERROR  },
                };
            }
        }
        public async Task<ResultViewModel<$EntityNameModifyModel>> Find($PK id)
        {
			var cacheKey = $"{CACHING_PREFIX}{id.GetHashCode()}";
            try
            {

                var cached = _cacheService.Get<ResultViewModel<$EntityNameModifyModel>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }

                var data = await _context.$EntitySetName.FindAsync(id);
                var result = new ResultViewModel<$EntityNameModifyModel>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
                    Data = data.Adapt<$EntityNameModifyModel>()
                };

                _cacheService.Set(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<$EntityNameModifyModel>
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = {  ResourcesKeys.DATA_LOAD_ERROR },
                };
            }
        }


        public async Task<AcknowledgeViewModel> Add($EntityNameModifyModel dto)
        {
            try
            {
                var $EntityVarName = new $EntityName(dto);
                var entity = await _context.$EntitySetName.AddAsync($EntityVarName);
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear("{CACHING_PREFIX}*");
                return new ()
                {

                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_SAVE_SUCCESS },
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_SAVE_ERROR },
                };
            }
        }

        public async Task<AcknowledgeViewModel> Update($EntityNameModifyModel dto)
        {
            try
            {
                var $EntityVarName = await _context.$EntitySetName.FindAsync(dto.Id);
                if ($EntityVarName == null)
                {
                    var exp = new KeyNotFoundException($"item number {dto.Id} does not Exist");
                    _logger.LogError(exp.Message, exp);
                    return new ResultViewModel<$EntityNameModifyModel>
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ResourcesKeys.ITEM_NOT_FOUND },
                    };
                }


                var entity = $EntityVarName.Update(dto);

                _ = await _context.SaveChangesAsync();

                _cacheService.Clear("{CACHING_PREFIX}*");

                return new ()
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_MODIFY_SUCCESS }
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_SAVE_ERROR },
                };
            }
        }

        public async Task<AcknowledgeViewModel> Delete($PK id)
        {
            try
            {
                var $EntityVarName = await _context.$EntitySetName.FindAsync(id);
                if ($EntityVarName == null)
                {
                    var exp = new KeyNotFoundException($"item number {id} does not Exist");
                    _logger.LogError(exp.Message, exp);
                    return new ResultViewModel<$EntityNameModifyModel>
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ResourcesKeys.ITEM_NOT_FOUND },
                    };
                }
                var entity = $EntityVarName.Delete();
                _context.Entry(entity).State = EntityState.Deleted;
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear("{CACHING_PREFIX}*");

                return new ()
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_REMOVE_SUCCESS },
                    Data = entity.Adapt<$EntityNameModifyModel>()
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_REMOVE_ERROR },
                };
            }
        }


    }
}
""";

    public const string AutoMapperController = """
using $SolutionName.AppApi.Controllers.v1;
using $SolutionName.Common;
using $SolutionName.AppApi.Services;
using $SolutionName.Core.Database.Tables;
using $SolutionName.Core.Models;
using $SolutionName.Services;
using $SolutionName.Core.Consts;
using $SolutionName.Common.Extensions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using $SolutionName.AppApi.Infrastrcture.Attributes;

using System.Security.Claims;

namespace $SolutionName.AppApi.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class $EntitySetNameController : BaseController
	{
		private readonly $EntityNameService _$EntityVarNameService;
		private readonly Translator _translator;
		private readonly ILogger<$EntitySetNameController> _logger;

		public $EntitySetNameController($EntityNameService $EntityVarNameService, Translator translator, ILogger<$EntitySetNameController> logger) : base(translator)
		{
			_$EntityVarNameService = $EntityVarNameService;
			_translator = translator;
			_logger = logger;
		}



		[HttpGet]
[RequiredPermission("Read")]
public async Task<IActionResult> GetAll([FromQuery] QueryViewModel query, CancellationToken cancellationToken)
{
	if (cancellationToken.IsCancellationRequested)
	{
		return BadRequest(new
		{
			IsSuccess = false,
			Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
		});
	}
	
	try
	{
		query.UserId = User.FindFirstValue("Id").ToGuid();
	   if(string.IsNullOrEmpty(query.Fields))
		{
			var data = await _$EntityVarNameService.All(query);
			data.Messages = [.. data.Messages.Select(m => _translator.Translate(m, Language))];
			if (data.IsSuccess)
				return Ok(data);

			return BadRequest(data);
		}

		var selectedData = await _$EntityVarNameService.Select(query);
		selectedData.Messages = [.. selectedData.Messages.Select(m => _translator.Translate(m, Language))];
		if (selectedData.IsSuccess)
			return Ok(selectedData);

		return BadRequest(selectedData);

	}
	catch (HttpException ex)
	{
		_logger.LogError(ex.Message, query, ex);
		return BadRequest(HandleHttpException<List<$EntityNameReadModel>>(ex));
	}
	catch (Exception ex)
	{

		_logger.LogError(ex.Message, ex);
		var result = new ResultViewModel<List<$EntityNameReadModel>>{
			IsSuccess = false,
			StatusCode = 500
		};
		return BadRequest(result);
	}
}

		[HttpGet("{id}")]
		[RequiredPermission("Read")]
		public async Task<IActionResult> GetOne($PK id, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return BadRequest(new
				{
					IsSuccess = false,
					Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
				});
			}
			
			try
			{
				var data = await _$EntityVarNameService.Find(id);
				data.Messages = data.Messages.Select(m => _translator.Translate(m, Language)).ToList();

				if (data.IsSuccess)

					return Ok(data);

				return BadRequest(data);

			}
			catch (HttpException ex)
			{

				_logger.LogError(ex.Message, ex);
				return BadRequest(HandleHttpException<$EntityNameReadModel>(ex));
			}
			catch (Exception ex)
			{

				_logger.LogError(ex.Message, ex);
			   var result = new ResultViewModel<List<$EntityNameReadModel>>{
					IsSuccess = false,
					StatusCode = 500
				};
				return BadRequest(result);
			}
		}



		[HttpPost]
		[RequiredPermission("Create")]
		public async Task<IActionResult> Post$EntityName([FromBody] $EntityNameModifyModel model, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return BadRequest(new
				{
					IsSuccess = false,
					Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
				});
			}
		   
			try
			{
				var data = await _$EntityVarNameService.Add(model);
				data.Messages = data.Messages.Select(m => _translator.Translate(m, Language)).ToList();

				if (data.IsSuccess)
					return Ok(data);

				return BadRequest(data);

			}
			catch (HttpException ex)
			{

				_logger.LogError(ex.Message, ex);
				return BadRequest(HandleHttpException(ex));
			}
			catch (Exception ex)
			{

				_logger.LogError(ex.Message, ex);
				 var result = new AcknowledgeViewModel
				 {
					IsSuccess = false,
					StatusCode = 500
				 };
				return BadRequest(result);
			}
		}

		[HttpPut("{id}")]
		[RequiredPermission("Update")]
		public async Task<IActionResult> Put$EntityName($PK id, [FromBody] $EntityNameModifyModel model, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return BadRequest(new
				{
					IsSuccess = false,
					Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
				});
			}
			
			
			try
			{
				if (model.Id != id)
				{
					var result = new AcknowledgeViewModel{
					  IsSuccess = false
					};
					result.Messages.Add(_translator.Translate("InvalidData", Language));
					return BadRequest(result);
				}
				var data = await _$EntityVarNameService.Update(model);
				data.Messages = data.Messages.Select(m => _translator.Translate(m, Language)).ToList();

				if (data.IsSuccess)
					return Ok(data);

				return BadRequest(data);

			}
			catch (HttpException ex)
			{

				_logger.LogError(ex.Message, ex);
				return BadRequest(HandleHttpException(ex));
			}
			catch (Exception ex)
			{

				_logger.LogError(ex.Message, ex);
				var result = new AcknowledgeViewModel
				 {
					IsSuccess = false,
					StatusCode = 500
				 };
				return BadRequest(result);
			}
		}

		[HttpDelete("{id}")]
		[RequiredPermission("Update,Delete")]
		public async Task<IActionResult> Delete$EntityName($PK id, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return BadRequest(new
				{
					IsSuccess = false,
					Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
				});
			}
			
			
			try
			{
				var data = await _$EntityVarNameService.Delete(id);
				data.Messages = data.Messages.Select(m => _translator.Translate(m, Language)).ToList();

				if (data.IsSuccess)
					return Ok(data);

				return BadRequest(data);

			}
			catch (HttpException ex)
			{

				_logger.LogError(ex.Message, ex);
				return BadRequest(HandleHttpException(ex));
			}
			catch (Exception ex)
			{

				_logger.LogError(ex.Message, ex);
				var result = new AcknowledgeViewModel
				 {
					IsSuccess = false,
					StatusCode = 500
				 };
				return BadRequest(result);
			}
		}


	}

}
""";

    public const string MapsterController = """
using $SolutionName.AppApi.Controllers.v1;
using $SolutionName.Common;
using $SolutionName.AppApi.Services;
using $SolutionName.Core.Database.Tables;
using $SolutionName.Core.Models;
using $SolutionName.Services;
using $SolutionName.Core.Consts;
using $SolutionName.Common.Extensions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using $SolutionName.AppApi.Infrastrcture.Attributes;

using $SolutionName.Core.Database.Notifications;

using System.Security.Claims;

namespace $SolutionName.AppApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class $EntitySetNameController : BaseController
    {
        private readonly $EntityNameService _$EntityVarNameService;
        private readonly Translator _translator;
        private readonly ILogger<$EntitySetNameController> _logger;

        public $EntitySetNameController($EntityNameService $EntityVarNameService, Translator translator, ILogger<$EntitySetNameController> logger) : base(translator)
        {
            _$EntityVarNameService = $EntityVarNameService;
            _translator = translator;
            _logger = logger;
        }



        [HttpGet]
[RequiredPermission("Read")]
public async Task<IActionResult> GetAll([FromQuery] QueryViewModel query, CancellationToken cancellationToken)
{
	if (cancellationToken.IsCancellationRequested)
	{
		return BadRequest(new
		{
			IsSuccess = false,
			Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
		});
	}
	
	try
	{
		query.UserId = User.FindFirstValue("Id").ToGuid();
	   if(string.IsNullOrEmpty(query.Fields))
		{
			var data = await _$EntityVarNameService.All(query);
			data.Messages = [.. data.Messages.Select(m => _translator.Translate(m, Language))];
			if (data.IsSuccess)
				return Ok(data);

			return BadRequest(data);
		}

		var selectedData = await _$EntityVarNameService.Select(query);
		selectedData.Messages = [.. selectedData.Messages.Select(m => _translator.Translate(m, Language))];
		if (selectedData.IsSuccess)
			return Ok(selectedData);

		return BadRequest(selectedData);

	}
	catch (HttpException ex)
	{
		_logger.LogError(ex.Message, query, ex);
		return BadRequest(HandleHttpException<List<$EntityNameReadModel>>(ex));
	}
	catch (Exception ex)
	{

		_logger.LogError(ex.Message, ex);
		var result = new ResultViewModel<List<$EntityNameReadModel>>{
			IsSuccess = false,
			StatusCode = 500
		};
		return BadRequest(result);
	}
}

        [HttpGet("{id}")]
		[RequiredPermission("Read")]
        public async Task<IActionResult> GetOne($PK id, CancellationToken cancellationToken)
        {
			if (cancellationToken.IsCancellationRequested)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
                });
            }
            
            try
            {
                var data = await _$EntityVarNameService.Find(id);
                data.Messages = data.Messages.Select(m => _translator.Translate(m, Language)).ToList();

                if (data.IsSuccess)

                    return Ok(data);

                return BadRequest(data);

            }
            catch (HttpException ex)
            {

                _logger.LogError(ex.Message, ex);
                return BadRequest(HandleHttpException<$EntityNameReadModel>(ex));
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
               var result = new ResultViewModel<List<$EntityNameReadModel>>{
					IsSuccess = false,
					StatusCode = 500
				};
                return BadRequest(result);
            }
        }



        [HttpPost]
		[RequiredPermission("Create")]
        public async Task<IActionResult> Post$EntityName([FromBody] $EntityNameModifyModel model, CancellationToken cancellationToken)
        {
			if (cancellationToken.IsCancellationRequested)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
                });
            }
           
            try
            {
                var data = await _$EntityVarNameService.Add(model);
                data.Messages = data.Messages.Select(m => _translator.Translate(m, Language)).ToList();

                if (data.IsSuccess)
                    return Ok(data);

                return BadRequest(data);

            }
            catch (HttpException ex)
            {

                _logger.LogError(ex.Message, ex);
                return BadRequest(HandleHttpException(ex));
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
				 var result = new AcknowledgeViewModel
				 {
					IsSuccess = false,
					StatusCode = 500
				 };
                return BadRequest(result);
            }
        }

        [HttpPut("{id}")]
		[RequiredPermission("Update")]
        public async Task<IActionResult> Put$EntityName($PK id, [FromBody] $EntityNameModifyModel model, CancellationToken cancellationToken)
        {
			if (cancellationToken.IsCancellationRequested)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
                });
            }
			
            
            try
            {
                if (model.Id != id)
                {
                   var result = new AcknowledgeViewModel{
                      IsSuccess = false
                    };
                    result.Messages.Add(_translator.Translate("InvalidData", Language));
                    return BadRequest(result);
                }
                var data = await _$EntityVarNameService.Update(model);
                data.Messages = data.Messages.Select(m => _translator.Translate(m, Language)).ToList();

                if (data.IsSuccess)
                    return Ok(data);

                return BadRequest(data);

            }
            catch (HttpException ex)
            {

                _logger.LogError(ex.Message, ex);
                return BadRequest(HandleHttpException(ex));
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                var result = new AcknowledgeViewModel
				 {
					IsSuccess = false,
					StatusCode = 500
				 };
                return BadRequest(result);
            }
        }

        [HttpDelete("{id}")]
		[RequiredPermission("Update,Delete")]
        public async Task<IActionResult> Delete$EntityName($PK id, CancellationToken cancellationToken)
        {
			if (cancellationToken.IsCancellationRequested)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Messages = new[] { _translator.Translate(ResourcesKeys.REQUEST_CANCELED, Language) }
                });
            }
			
            
            try
            {
                var data = await _$EntityVarNameService.Delete(id);
                data.Messages = data.Messages.Select(m => _translator.Translate(m, Language)).ToList();

                if (data.IsSuccess)
                    return Ok(data);

                return BadRequest(data);

            }
            catch (HttpException ex)
            {

                _logger.LogError(ex.Message, ex);
                return BadRequest(HandleHttpException(ex));
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                var result = new AcknowledgeViewModel
				 {
					IsSuccess = false,
					StatusCode = 500
				 };
                return BadRequest(result);
            }
        }


    }

}
""";

    public static string GetDefault(MapperKind mapper, string templateName) =>
        (mapper, templateName) switch
        {
            (MapperKind.AutoMapper, "ModifyModel") => AutoMapperModifyModel,
            (MapperKind.Mapster, "ModifyModel") => MapsterModifyModel,
            (MapperKind.AutoMapper, "ReadModel") => AutoMapperReadModel,
            (MapperKind.Mapster, "ReadModel") => MapsterReadModel,
            (_, "Validator") => Validator,
            (MapperKind.AutoMapper, "Entity") => AutoMapperEntity,
            (MapperKind.Mapster, "Entity") => MapsterEntity,
            (_, "EntityConfig") => EntityConfig,
            (MapperKind.AutoMapper, "Config") => AutoMapperConfig,
            (_, "DbSetEntry") => DbSetEntry,
            (_, "ConfigEntry") => ConfigEntry,
            (MapperKind.AutoMapper, "Service") => AutoMapperService,
            (MapperKind.Mapster, "Service") => MapsterService,
            (MapperKind.AutoMapper, "Controller") => AutoMapperController,
            (MapperKind.Mapster, "Controller") => MapsterController,
            _ => throw new ArgumentException($"Unknown template '{templateName}' for mapper '{mapper}'.", nameof(templateName))
        };
}
