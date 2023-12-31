﻿using System;
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


    public class $EntityNameService
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
                    Messages = { ""data loaded successfully"" },
                    Total = count,
                    PageSize = query.PageSize,
                    PageNumber = query.PageNumber,
                    Filter = query.Filter,
                    Data = _mapper.Map<List<$EntityNameDto>>(list)
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
                    Messages = { ""data loaded successfully"" },
                    Data = _mapper.Map<$EntityNameDto>(data)
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
                    Messages = { ""data loaded successfully"" },
                    Data = _mapper.Map<$EntityNameDto>(data)
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
                var tag = new $EntityName(dto);
                var entity = await _context.$EntitySetName.AddAsync(tag);
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""$EntityName:"");
                return new ResultViewModel<$EntityNameDto>
                {

                    Data = _mapper.Map<$EntityNameDto>(entity.Entity),
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data saved successfully"" },
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
                var tag = await _context.$EntitySetName.FindAsync(dto.Id);
                if (tag == null)
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


                var entity = tag.Update(dto);

                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""$EntityName:"");

                return new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data modified successfully"" },
                    Data = _mapper.Map<$EntityNameDto>(entity)
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
                var tag = await _context.$EntitySetName.FindAsync(id);
                if (tag == null)
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
                var entity = tag.Delete();
                _ = await _context.SaveChangesAsync();

                _cacheService.Clear(""$EntityName:"");

                return new ResultViewModel<$EntityNameDto>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Messages = { ""data removed successfully"" },
                    Data = _mapper.Map<$EntityNameDto>(entity)
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
