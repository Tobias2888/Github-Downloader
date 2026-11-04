using LoggerLib;
using Microsoft.AspNetCore.Mvc;
using GithubDownloader.Web.DTOs;
using GithubDownloader.Web.Services;

namespace GithubDownloader.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ConfigController : ControllerBase
{
    private readonly IGithubDownloaderService _service;

    public ConfigController(IGithubDownloaderService service)
    {
        _service = service;
    }

    [HttpPost("export")]
    [ProducesResponseType(typeof(ConfigOperationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConfigOperationResponse>> ExportConfig([FromBody] ExportConfigRequest request)
    {
        try
        {
            ConfigOperationResponse result = await _service.ExportConfigAsync(request.DestinationPath);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _ = Logger.LogE($"Failed to export configuration ({ex.GetType().Name})");
            return BadRequest(new ErrorResponse("Failed to export configuration", ex.Message));
        }
    }

    [HttpPost("import")]
    [ProducesResponseType(typeof(ConfigOperationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConfigOperationResponse>> ImportConfig([FromBody] ImportConfigRequest request)
    {
        try
        {
            ConfigOperationResponse result = await _service.ImportConfigAsync(request.SourcePath);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _ = Logger.LogE($"Failed to import configuration ({ex.GetType().Name})");
            return BadRequest(new ErrorResponse("Failed to import configuration", ex.Message));
        }
    }
}