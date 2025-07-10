using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.AutoMapper
{
    public static class Service
    {
        public const string Template = @"using AutoMapper;

using {{ SolutionName }}.Core.Database.Tables;
using {{ SolutionName }}.Core.Models;
using {{ SolutionName }}.EntityFramework;
using {{ SolutionName }}.Services.Chache;
using {{ SolutionName }}.Core.Consts;

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


    public class {{ EntityName }}Service : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<{{ EntityName }}Service> _logger;
        private readonly ICacheService _cacheService;
        private readonly IMapper _mapper;

        private readonly string CACHING_PREFIX = ""{{ EntityName }}:"";

        public {{ EntityName }}Service(ApplicationDbContext context, ILogger<{{ EntityName }}Service> logger, ICacheService cacheService, IMapper mapper)
        {
            _context = context;
            _logger = logger;
            _cacheService = cacheService;
            _mapper = mapper;
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

                query.Order = string.IsNullOrEmpty(query.Order) ? ""CreatedDate Desc"" : query.Order;
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
                    Data = _mapper.Map<List<{{ EntityName }}ReadModel>>(list)
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
                    Data = _mapper.Map<{{ EntityName }}ModifyModel>(data)
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
                    Messages = {  ResourcesKeys.DATA_LOAD_SUCCESS },
                    Data = _mapper.Map<{{ EntityName }}ModifyModel>(data)
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


        public async Task<AcknowledgeViewModel> Add({{ EntityName }}ModifyModel model)
        {
            try
            {
                var record = new {{ EntityName }}(model);
                var entity = await _context.{{ EntitySetName }}.AddAsync(record);
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

        public async Task<AcknowledgeViewModel> Update({{ EntityName }}ModifyModel model)
        {
            try
            {
                var record = await _context.{{ EntitySetName }}.FindAsync(model.Id);
                if (record == null)
                {
                    var exp = new KeyNotFoundException($""item number {model.Id} does not Exist"");
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
                var record = await _context.{{ EntitySetName }}.FindAsync(id);
                if (record == null)
                {
                    var exp = new KeyNotFoundException($""item number {id} does not Exist"");
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

                _cacheService.Clear(""{CACHING_PREFIX}*"");

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
}";
    }
}