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

using $SolutionName.Core.Database.Tables;
using $SolutionName.Core.Models;
using $SolutionName.EntityFramework;
using $SolutionName.Services.Chache;

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

        public $EntityNameService(ApplicationDbContext context, ILogger<$EntityNameService> logger, ICacheService cacheService, IMapper mapper)
        {
            _context = context;
            _logger = logger;
            _cacheService = cacheService;
            _mapper = mapper;
        }



        public async Task<ResultViewModel<List<$EntityNameReadModel>>> All(QueryViewModel query)
        {
			var cacheKey = $""$EntityName:GetAll_{query.GetHashCode()}"".GetHashCode().ToString();
            try
            {
                var cached = _cacheService.Get<ResultViewModel<List<$EntityNameReadModel>>>(cacheKey);

                if (cached != null)
                {
                    return cached;
                }


                var data = _context.$EntitySetName.AsQueryable();
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

                var result = new ResultViewModel<List<$EntityNameReadModel>>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data loaded successfully"" },
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
                    Messages = { ""error while reading data."" },
                    Filter = query.Filter,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize,
                };
            }
        }

        public async Task<ResultViewModel<$EntityNameModifyModel>> Find(Expression<Func<$EntityName, bool>> predicate)
        {
			var cacheKey = $""$EntityName:Find_{predicate.Body}"".GetHashCode().ToString();
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
                    Messages = { ""data loaded successfully"" },
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
                    Messages = { ""error while loading data"" },
                };
            }
        }
        public async Task<ResultViewModel<$EntityNameModifyModel>> Find($PK id)
        {
			var cacheKey = $""$EntityName:Find_{id}"".GetHashCode().ToString();
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
                    Messages = { ""data loaded successfully"" },
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
                    Messages = { ""error while loading data"" },
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

                _cacheService.Clear(""$EntityName:"");
                return new ()
                {

                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data saved successfully"" },
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ""error while saving data"" },
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
                    var exp = new KeyNotFoundException($""item number {model.Id} does not Exist"");
                    _logger.LogError(exp.Message, exp);
                    return new ()
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ""Item Not Found"" },
                    };
                }


                var entity = record.Update(model);

                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""$EntityName:"");

                return new ()
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data modified successfully"" }
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ""error while saving data"" },
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
                    var exp = new KeyNotFoundException($""item number {id} does not Exist"");
                    _logger.LogError(exp.Message, exp);
                    return new ()
                    {
                        IsSuccess = false,
                        StatusCode = 500,
                        Messages = { ""Item Not Found"" },
                    };
                }
                var entity = record.Delete();
                _context.Entry(entity).State = EntityState.Deleted;
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""$EntityName:"");

                return new ()
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data removed successfully"" }
                };

            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                return new ()
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    Messages = { ""error while removing data"" },
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
