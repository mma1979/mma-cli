using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Templates.AutoMapper
{
    public static class Controller
    {
        public static string Template = @"using $SolutionName.AppApi.Controllers.v1;
using $SolutionName.Common;
using $SolutionName.Common.Helpers;
using $SolutionName.Core.Database.Tables;
using $SolutionName.Core.Models;
using $SolutionName.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using $SolutionName.AppApi.Infrastrcture;

namespace $SolutionName.AppApi.Controllers
{
    [Route(""api/[controller]"")]
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
		[RequiredPermission(""Read"")]
        public async Task<ActionResult<ResultViewModel<List<$EntityNameDto>>>> GetAll([FromQuery] QueryViewModel query, CancellationToken cancellationToken)
        {
			if (cancellationToken.IsCancellationRequested)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Messages = new[] { ""Request has been canceled"" }
                });
            }
            var result = new ResultViewModel<List<$EntityNameDto>>();
            try
            {
                query.UserId = UserId;
                var data = await _$EntityVarNameService.All(query);
                data.Messages = data.Messages.Select(m => _translator.Translate(m, Language)).ToList();
                if (data.IsSuccess)
                    return Ok(data);

                return BadRequest(data);

            }
            catch (HttpException ex)
            {
                _logger.LogError(ex.Message, query, ex);
                return BadRequest(HandleHttpException<List<$EntityNameDto>>(ex));
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                result.IsSuccess = false;
                result.StatusCode = 500;
                return BadRequest(result);
            }
        }

        [HttpGet(""{id}"")]
		[RequiredPermission(""Read"")]
        public async Task<ActionResult<ResultViewModel<$EntityNameDto>>> GetOne($PK id, CancellationToken cancellationToken)
        {
			if (cancellationToken.IsCancellationRequested)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Messages = new[] { ""Request has been canceled"" }
                });
            }
            var result = new ResultViewModel<$EntityNameDto>();
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
                return BadRequest(HandleHttpException<$EntityNameDto>(ex));
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                result.IsSuccess = false;
                result.StatusCode = 500;
                return BadRequest(result);
            }
        }



        [HttpPost]
		[RequiredPermission(""Create"")]
        public async Task<ActionResult<ResultViewModel<$EntityNameDto>>> Post$EntityName([FromBody] $EntityNameDto model, CancellationToken cancellationToken)
        {
			if (cancellationToken.IsCancellationRequested)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Messages = new[] { ""Request has been canceled"" }
                });
            }
            var result = new ResultViewModel<$EntityName>();
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
                return BadRequest(HandleHttpException<$EntityNameDto>(ex));
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                result.IsSuccess = false;
                result.StatusCode = 500;
                return BadRequest(result);
            }
        }

        [HttpPut(""{id}"")]
		[RequiredPermission(""Update"")]
        public async Task<ActionResult<ResultViewModel<$EntityNameDto>>> Put$EntityName($PK id, [FromBody] $EntityNameDto model, CancellationToken cancellationToken)
        {
			if (cancellationToken.IsCancellationRequested)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Messages = new[] { ""Request has been canceled"" }
                });
            }
			
            var result = new ResultViewModel<$EntityName>();
            try
            {
                if (model.Id != id)
                {
                    result.IsSuccess = false;
                    result.Messages.Add(_translator.Translate(""InvalidData"", Language));
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
                return BadRequest(HandleHttpException<$EntityNameDto>(ex));
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                result.IsSuccess = false;
                result.StatusCode = 500;
                return BadRequest(result);
            }
        }

        [HttpDelete(""{id}"")]
		[RequiredPermission(""Update,Delete"")]
        public async Task<ActionResult<ResultViewModel<$EntityNameDto>>> Delete$EntityName($PK id, CancellationToken cancellationToken)
        {
			if (cancellationToken.IsCancellationRequested)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Messages = new[] { ""Request has been canceled"" }
                });
            }
			
            var result = new ResultViewModel<$EntityNameDto>();
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
                return BadRequest(HandleHttpException<$EntityNameDto>(ex));
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message, ex);
                result.IsSuccess = false;
                result.StatusCode = 500;
                return BadRequest(result);
            }
        }


    }

}";
    }
}
