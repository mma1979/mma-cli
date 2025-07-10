using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.Mappster
{
    public static class Service
    {
        public const string Template = @"using {{ SolutionName }}.Core.Database.Tables;
using {{ SolutionName }}.Core.Models;
using {{ SolutionName }}.EntityFramework;
using {{ SolutionName }}.Services.Chache;
using {{ SolutionName }}.Core.Consts;

using Mapster;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace {{ SolutionName }}.Services
{


    public class {{ EntityName }}Service
    {
		private readonly ApplicationDbContext _context;
        private readonly ILogger<{{ EntityName }}Service> _logger;
        private readonly ICacheService _cacheService;

        private readonly string CACHING_PREFIX = ""{{ EntityName }}:"";

        public {{ EntityName }}Service(ApplicationDbContext context, ILogger<{{ EntityName }}Service> logger, ICacheService cacheService)
        {
            _context = context;
            _logger = logger;
            _cacheService = cacheService;
        }



        public async Task<ResultViewModel<List<{{ EntityName }}ReadModel>>> All(QueryViewModel query)
        {
			var cacheKey = $""{CACHING_PREFIX}{query.GetHashCode()}"";
            try
            {
                var cached = _cacheService.Get<ResultViewModel<List<{{ EntityName }}ReadModel>>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }


                var data = query.ShowAll ?
                        _context.{{ EntitySetName }}.IgnoreQueryFilters().AsQueryable() :
                        _context.{{ EntitySetName }}.AsQueryable();
                if (!string.IsNullOrEmpty(query.Filter))
                {
                    data = data.Where(query.Filter);
                }

                query.Order = string.IsNullOrEmpty(query.Order) ? ""Id Desc"" : query.Order;
                data = data.OrderBy(query.Order);

                var page = query.PageNumber <= 0 ? data :
                           data.Skip((query.PageNumber - 1) * query.PageSize)
                           .Take(query.PageSize);

                var count = await data.CountAsync();
                var list = await page.ToListAsync();

                var result = new ResultViewModel<List<{{ EntityName }}ReadModel>>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
                    Total = count,
                    PageSize = query.PageSize,
                    PageNumber = query.PageNumber,
                    Filter = query.Filter,
                    Data = list.Adapt<List<{{ EntityName }}ReadModel>>()
                };

                _cacheService.Set(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<List<{{ EntityName }}ReadModel>>
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

        public async Task<ResultViewModel<{{ EntityName }}ModifyModel>> Find(Expression<Func<{{ EntityName }}, bool>> predicate)
        {
			var cacheKey = $""{CACHING_PREFIX}{predicate.Body.GetHashCode()}"";
            try
            {
                var cached = _cacheService.Get<ResultViewModel<{{ EntityName }}ModifyModel>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }

                var data = await _context.{{ EntitySetName }}.SingleOrDefaultAsync(predicate);
                var result = new ResultViewModel<{{ EntityName }}ModifyModel>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
                    Data = data.Adapt<{{ EntityName }}ModifyModel>()
                };

                _cacheService.Set(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<{{ EntityName }}ModifyModel>
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ResourcesKeys.DATA_LOAD_ERROR  },
                };
            }
        }
        public async Task<ResultViewModel<{{ EntityName }}ModifyModel>> Find({{ PK }} id)
        {
			var cacheKey = $""{CACHING_PREFIX}{id.GetHashCode()}"";
            try
            {

                var cached = _cacheService.Get<ResultViewModel<{{ EntityName }}ModifyModel>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }

                var data = await _context.{{ EntitySetName }}.FindAsync(id);
                var result = new ResultViewModel<{{ EntityName }}ModifyModel>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_LOAD_SUCCESS },
                    Data = data.Adapt<{{ EntityName }}ModifyModel>()
                };

                _cacheService.Set(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<{{ EntityName }}ModifyModel>
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = {  ResourcesKeys.DATA_LOAD_ERROR },
                };
            }
        }


        public async Task<AcknowledgeViewModel> Add({{ EntityName }}ModifyModel dto)
        {
            try
            {
                var {{ EntityVarName }} = new {{ EntityName }}(dto);
                var entity = await _context.{{ EntitySetName }}.AddAsync({{ EntityVarName }});
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""{CACHING_PREFIX}*"");
                return new ()
                {

                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_SAVE_SUCCESS },
                };

            }
            catch (HttpException ex)
            {

                _logger.LogError(ex.Message, ex);
                return new()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = JsonConvert.DeserializeObject<List<string>>(ex.Message),
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

        public async Task<AcknowledgeViewModel> Update({{ EntityName }}ModifyModel dto)
        {
            try
            {
                var {{ EntityVarName }} = await _context.{{ EntitySetName }}.FindAsync(dto.Id);
                if ({{ EntityVarName }} == null)
                {
                    var exp = new KeyNotFoundException($""item number {dto.Id} does not Exist"");
                    _logger.LogError(exp.Message, exp);
                    return new ResultViewModel<{{ EntityName }}ModifyModel>
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ResourcesKeys.ITEM_NOT_FOUND },
                    };
                }


                var entity = {{ EntityVarName }}.Update(dto);

                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""{CACHING_PREFIX}*"");

                return new ()
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_MODIFY_SUCCESS }
                };

            }
            catch (HttpException ex)
            {

                _logger.LogError(ex.Message, ex);
                return new()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = JsonConvert.DeserializeObject<List<string>>(ex.Message),
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

        public async Task<AcknowledgeViewModel> Delete({{ PK }} id)
        {
            try
            {
                var {{ EntityVarName }} = await _context.{{ EntitySetName }}.FindAsync(id);
                if ({{ EntityVarName }} == null)
                {
                    var exp = new KeyNotFoundException($""item number {id} does not Exist"");
                    _logger.LogError(exp.Message, exp);
                    return new ResultViewModel<{{ EntityName }}ModifyModel>
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ResourcesKeys.ITEM_NOT_FOUND },
                    };
                }
                var entity = {{ EntityVarName }}.Delete();
                _context.Entry(entity).State = EntityState.Deleted;
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""{CACHING_PREFIX}*"");

                return new ()
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ResourcesKeys.DATA_REMOVE_SUCCESS },
                    Data = entity.Adapt<{{ EntityName }}ModifyModel>()
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
}";
    }
}