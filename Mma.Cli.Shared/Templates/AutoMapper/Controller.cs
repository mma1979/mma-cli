using System.Text;

using Mma.Cli.Shared.Builders;

namespace Mma.Cli.Shared.Templates.AutoMapper
{
	public static class Controller
	{
		public static string Template => Render(EntityCodeGenOptions.Default);

		public static string Render(EntityCodeGenOptions options)
		{
			var sb = new StringBuilder();
			sb.Append(Header);
			if (options.GetAll && options.Select)
			{
				sb.Append(GetAllAndSelectAction);
			}
			else if (options.GetAll)
			{
				sb.Append(GetAllOnlyAction);
			}
			else if (options.Select)
			{
				sb.Append(SelectOnlyAction);
			}

			if (options.Find) sb.Append(GetOneAction);
			if (options.Add) sb.Append(PostAction);
			if (options.Update) sb.Append(PutAction);
			if (options.Delete) sb.Append(DeleteAction);
			sb.Append(Footer);
			return sb.ToString();
		}

		private const string Header = @"using $SolutionName.AppApi.Controllers.v1;
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

using Asp.Versioning;
using System.Security.Claims;

namespace $SolutionName.AppApi.Controllers.v1
{
	[Route(""api/v{version:apiVersion}/[controller]"")]
	[ApiVersion(1.0)]
	[ApiController]
	[Resource(""$EntityResourceName"")]
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
";

		private const string GetAllAndSelectAction = @"

		[HttpGet]
		[RequiredPermission(""Read"")]
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
				query.UserId = User.FindFirstValue(""Id"").ToGuid();
				if (string.IsNullOrEmpty(query.Fields))
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
				var result = new ResultViewModel<List<$EntityNameReadModel>>
				{
					IsSuccess = false,
					StatusCode = 500
				};
				return BadRequest(result);
			}
		}
";

		private const string GetAllOnlyAction = @"

		[HttpGet]
		[RequiredPermission(""Read"")]
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
				query.UserId = User.FindFirstValue(""Id"").ToGuid();
				var data = await _$EntityVarNameService.All(query);
				data.Messages = [.. data.Messages.Select(m => _translator.Translate(m, Language))];
				if (data.IsSuccess)
					return Ok(data);

				return BadRequest(data);
			}
			catch (HttpException ex)
			{
				_logger.LogError(ex.Message, query, ex);
				return BadRequest(HandleHttpException<List<$EntityNameReadModel>>(ex));
			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, ex);
				var result = new ResultViewModel<List<$EntityNameReadModel>>
				{
					IsSuccess = false,
					StatusCode = 500
				};
				return BadRequest(result);
			}
		}
";

		private const string SelectOnlyAction = @"

		[HttpGet]
		[RequiredPermission(""Read"")]
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
				query.UserId = User.FindFirstValue(""Id"").ToGuid();
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
				var result = new ResultViewModel<List<$EntityNameReadModel>>
				{
					IsSuccess = false,
					StatusCode = 500
				};
				return BadRequest(result);
			}
		}
";

		private const string GetOneAction = @"
		[HttpGet(""{id}"")]
		[RequiredPermission(""Read"")]
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
				var result = new ResultViewModel<List<$EntityNameReadModel>>
				{
					IsSuccess = false,
					StatusCode = 500
				};
				return BadRequest(result);
			}
		}
";

		private const string PostAction = @"
		[HttpPost]
		[RequiredPermission(""Create"")]
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
";

		private const string PutAction = @"
		[HttpPut(""{id}"")]
		[RequiredPermission(""Update"")]
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
					var result = new AcknowledgeViewModel
					{
						IsSuccess = false
					};
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
";

		private const string DeleteAction = @"
		[HttpDelete(""{id}"")]
		[RequiredPermission(""Delete"")]
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
";

		private const string Footer = @"
	}

}";
	}
}
