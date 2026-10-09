using Microsoft.AspNetCore.Mvc;
using Github_Downloader_Web.DTOs;
using Github_Downloader_Web.Services;

namespace Github_Downloader_Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class UpdatesController : ControllerBase
{
    private readonly IGithubDownloaderService _service;
    private readonly OperationTracker _tracker;
    private readonly ILogger<UpdatesController> _logger;

    public UpdatesController(IGithubDownloaderService service, OperationTracker tracker, ILogger<UpdatesController> logger)
    {
        _service = service;
        _tracker = tracker;
        _logger = logger;
    }

    [HttpGet("progress")]
    [ProducesResponseType(typeof(OperationProgressResponse), StatusCodes.Status200OK)]
    public ActionResult<OperationProgressResponse> GetProgress()
    {
        return Ok(_tracker.Snapshot());
    }

    [HttpGet("rootstatus")]
    [ProducesResponseType(typeof(RootStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RootStatusResponse>> GetRootStatus()
    {
        return Ok(await _service.GetRootStatusAsync());
    }

    [HttpPost("search")]
    [ProducesResponseType(typeof(SearchUpdatesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchUpdatesResponse>> SearchUpdates([FromBody] SearchUpdatesRequest request)
    {
        try
        {
            var result = await _service.SearchUpdatesAsync(request.RepoUrls);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search for updates");
            return BadRequest(new ErrorResponse("Failed to search for updates", ex.Message));
        }
    }

    [HttpPost("download")]
    [ProducesResponseType(typeof(DownloadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DownloadResponse>> DownloadAssets([FromBody] DownloadRequest request)
    {
        try
        {
            var result = await _service.DownloadAssetsAsync(request.RepoUrls, request.DownloadAnyways);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download assets");
            return BadRequest(new ErrorResponse("Failed to download assets", ex.Message));
        }
    }

    [HttpPost("install")]
    [ProducesResponseType(typeof(InstallResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InstallResponse>> InstallAssets([FromBody] InstallRequest request)
    {
        try
        {
            var result = await _service.InstallAssetsAsync(request.RepoUrls, request.DownloadAnyways, password: request.Password);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install assets");
            return BadRequest(new ErrorResponse("Failed to install assets", ex.Message));
        }
    }
}