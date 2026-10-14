using LoggerLib;
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

    public UpdatesController(IGithubDownloaderService service, OperationTracker tracker)
    {
        _service = service;
        _tracker = tracker;
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
            SearchUpdatesResponse result = await _service.SearchUpdatesAsync(request.RepoUrls);
            return Ok(result);
        }
        catch (Exception ex)
        {
            Logger.LogE($"Failed to search for updates ({ex.GetType().Name})");
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
            DownloadResponse result = await _service.DownloadAssetsAsync(request.RepoUrls, request.DownloadAnyways);
            return Ok(result);
        }
        catch (Exception ex)
        {
            Logger.LogE($"Failed to download assets ({ex.GetType().Name})");
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
            InstallResponse result = await _service.InstallAssetsAsync(request.RepoUrls, request.DownloadAnyways, password: request.Password);
            return Ok(result);
        }
        catch (Exception ex)
        {
            Logger.LogE($"Failed to install assets ({ex.GetType().Name})");
            return BadRequest(new ErrorResponse("Failed to install assets", ex.Message));
        }
    }
}