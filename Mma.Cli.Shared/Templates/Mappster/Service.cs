using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.Mappster
{
    public static class Service
    {
        public const string Template = @"using $SolutionName.Core.Database.Tables;
using $SolutionName.Core.Models;
using $SolutionName.EntityFramework;
using $SolutionName.Services.Chache;

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

        public $EntityNameService(ApplicationDbContext context, ILogger<$EntityNameService> logger, ICacheService cacheService)
        {
            _context = context;
            _logger = logger;
            _cacheService = cacheService;
        }



        public async Task<ResultViewModel<List<$EntityNameDto>>> All(QueryViewModel query)
        {
            try
            {
                var cached = _cacheService.Get<ResultViewModel<List<$EntityNameDto>>>($""$EntityName:GetAll_{query.GetHashCode()}"");

                if (cached != null)
                {
                    return cached;
                }


                var data = _context.$EntitySetName.AsQueryable();
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

                var result = new ResultViewModel<List<$EntityNameDto>>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data loaded successfuly"" },
                    Total = count,
                    PageSize = query.PageSize,
                    PageNumber = query.PageNumber,
                    Filter = query.Filter,
                    Data = list.Adapt<List<$EntityNameDto>>()
                };

                _cacheService.Set($""$EntityName:GetAll_{query.GetHashCode()}"", result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<List<$EntityNameDto>>
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ""error while reading data."" },
                    Filter = query.Filter,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize,
                };
            }
        }

        public async Task<ResultViewModel<$EntityNameDto>> Find(Expression<Func<$EntityName, bool>> predicate)
        {
            try
            {
                var cached = _cacheService.Get<ResultViewModel<$EntityNameDto>>($""$EntityName:Find_{predicate.Body}"");

                if (cached != null)
                {
                    return cached;
                }

                var data = await _context.$EntitySetName.SingleOrDefaultAsync(predicate);
                var result = new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data loaded successfuly"" },
                    Data = data.Adapt<$EntityNameDto>()
                };

                _cacheService.Set($""$EntityName:Find_{predicate.Body}"", result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ""error while loading data"" },
                };
            }
        }
        public async Task<ResultViewModel<$EntityNameDto>> Find($PK id)
        {
            try
            {

                var cached = _cacheService.Get<ResultViewModel<$EntityNameDto>>($""$EntityName:Find_{id}"");

                if (cached != null)
                {
                    return cached;
                }

                var data = await _context.$EntitySetName.FindAsync(id);
                var result = new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data loaded successfuly"" },
                    Data = data.Adapt<$EntityNameDto>()
                };

                _cacheService.Set($""$EntityName:Find_{id}"", result);

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ""error while loading data"" },
                };
            }
        }


        public async Task<ResultViewModel<$EntityNameDto>> Add($EntityNameDto dto)
        {
            try
            {
                var $EntityVarName = new $EntityName(dto);
                var entity = await _context.$EntitySetName.AddAsync($EntityVarName);
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""$EntityName:"");
                return new ResultViewModel<$EntityNameDto>
                {

                    Data = entity.Entity.Adapt<$EntityNameDto>(),
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data saved successfuly"" },
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ""error while saving data"" },
                };
            }
        }

        public async Task<ResultViewModel<$EntityNameDto>> Update($EntityNameDto dto)
        {
            try
            {
                var $EntityVarName = await _context.$EntitySetName.FindAsync(dto.Id);
                if ($EntityVarName == null)
                {
                    var exp = new KeyNotFoundException($""item number {dto.Id} does not Exist"");
                    _logger.LogError(exp.Message, exp);
                    return new ResultViewModel<$EntityNameDto>
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ""Item Not Found"" },
                    };
                }


                var entity = $EntityVarName.Update(dto);

                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""$EntityName:"");

                return new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data modified successfuly"" },
                    Data = entity.Adapt<$EntityNameDto>()
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""error while saving data"" },
                };
            }
        }

        public async Task<ResultViewModel<$EntityNameDto>> Delete($PK id)
        {
            try
            {
                var $EntityVarName = await _context.$EntitySetName.FindAsync(id);
                if ($EntityVarName == null)
                {
                    var exp = new KeyNotFoundException($""item number {id} does not Exist"");
                    _logger.LogError(exp.Message, exp);
                    return new ResultViewModel<$EntityNameDto>
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ""Item Not Found"" },
                    };
                }
                var entity = $EntityVarName.Delete();
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""$EntityName:"");

                return new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data removed successfuly"" },
                    Data = entity.Adapt<$EntityNameDto>()
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""error while removing data"" },
                };
            }
        }


    }
}";
    }
}
