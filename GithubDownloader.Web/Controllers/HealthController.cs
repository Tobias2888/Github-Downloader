using Microsoft.AspNetCore.Mvc;
using GithubDownloader.Web.DTOs;
using GithubDownloader.Web.Services;

namespace GithubDownloader.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly IGithubDownloaderService _service;

    public HealthController(IGithubDownloaderService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<HealthResponse>> GetHealth()
    {
        var health = await _service.GetHealthAsync();
        return Ok(health);
    }

    [HttpGet("ready")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetReadiness()
    {
        try
        {
            var health = await _service.GetHealthAsync();
            return Ok(health);
        }
        catch
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Not Ready" });
        }
    }

    [HttpGet("live")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetLiveness()
    {
        return Ok(new { status = "Alive" });
    }
}