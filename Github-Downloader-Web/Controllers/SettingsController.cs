using LoggerLib;
using Microsoft.AspNetCore.Mvc;
using Github_Downloader_Web.DTOs;
using Github_Downloader_Web.Services;

namespace Github_Downloader_Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SettingsController : ControllerBase
{
    private readonly IGithubDownloaderService _service;

    public SettingsController(IGithubDownloaderService service)
    {
        _service = service;
    }

    [HttpGet("info")]
    [ProducesResponseType(typeof(SettingsInfoResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SettingsInfoResponse>> GetInfo()
    {
        SettingsInfoResponse info = await _service.GetSettingsInfoAsync();
        return Ok(info);
    }

    [HttpGet("pat")]
    [ProducesResponseType(typeof(PatStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PatStatusResponse>> GetPatStatus()
    {
        PatStatusResponse status = await _service.GetPatStatusAsync();
        return Ok(status);
    }

    [HttpPost("pat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPat([FromBody] SetPatRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Pat))
                return BadRequest(new ErrorResponse("Invalid request", "Personal access token cannot be empty"));

            await _service.StorePatAsync(request.Pat);
            return NoContent();
        }
        catch (Exception ex)
        {
            Logger.LogE($"Failed to store personal access token ({ex.GetType().Name})");
            return BadRequest(new ErrorResponse("Failed to store personal access token", ex.Message));
        }
    }

    [HttpDelete("pat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemovePat()
    {
        await _service.ClearPatAsync();
        return NoContent();
    }

    [HttpPost("clear-data")]
    [ProducesResponseType(typeof(ClearDataResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClearDataResponse>> ClearData()
    {
        ClearDataResponse result = await _service.ClearAllDataAsync();
        return Ok(result);
    }
}