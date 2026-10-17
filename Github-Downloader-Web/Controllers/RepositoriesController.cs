using LoggerLib;
using Microsoft.AspNetCore.Mvc;
using Github_Downloader_Web.DTOs;
using Github_Downloader_Web.Services;

namespace Github_Downloader_Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RepositoriesController : ControllerBase
{
    private readonly IGithubDownloaderService _service;

    public RepositoriesController(IGithubDownloaderService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ReposListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReposListResponse>> GetAll()
    {
        ReposListResponse result = await _service.GetAllReposAsync();
        return Ok(result);
    }

    [HttpGet("{repoUrl}")]
    [ProducesResponseType(typeof(RepoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RepoResponse>> Get(string repoUrl)
    {
        string decodedUrl = Uri.UnescapeDataString(repoUrl);
        RepoResponse? repo = await _service.GetRepoAsync(decodedUrl);
        if (repo == null)
            return NotFound(new ErrorResponse("Repository not found", $"No repository found with URL: {decodedUrl}"));
        
        return Ok(repo);
    }

    [HttpPost]
    [ProducesResponseType(typeof(RepoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RepoResponse>> Add([FromBody] AddRepoRequest request)
    {
        try
        {
            RepoResponse repo = await _service.AddRepoAsync(request.RepoUrl);
            return CreatedAtAction(nameof(Get), new { repoUrl = Uri.EscapeDataString(repo.Url) }, repo);
        }
        catch (Exception ex)
        {
            Logger.LogE($"Failed to add repository ({ex.GetType().Name})");
            return BadRequest(new ErrorResponse("Failed to add repository", ex.Message));
        }
    }

    [HttpPost("by-name")]
    [ProducesResponseType(typeof(RepoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RepoResponse>> AddByName([FromBody] AddRepoByNameRequest request)
    {
        try
        {
            RepoResponse repo = await _service.AddRepoAsync(request.PublisherName, request.RepoName);
            return CreatedAtAction(nameof(Get), new { repoUrl = Uri.EscapeDataString(repo.Url) }, repo);
        }
        catch (Exception ex)
        {
            Logger.LogE($"Failed to add repository by name ({ex.GetType().Name})");
            return BadRequest(new ErrorResponse("Failed to add repository", ex.Message));
        }
    }

    [HttpPut("{repoUrl}")]
    [ProducesResponseType(typeof(RepoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RepoResponse>> Update(string repoUrl, [FromBody] UpdateRepoRequest request)
    {
        string decodedUrl = Uri.UnescapeDataString(repoUrl);
        try
        {
            RepoResponse? repo = await _service.UpdateRepoAsync(decodedUrl, request);
            if (repo == null)
                return NotFound(new ErrorResponse("Repository not found", $"No repository found with URL: {decodedUrl}"));
            
            return Ok(repo);
        }
        catch (Exception ex)
        {
            Logger.LogE($"Failed to update repository ({ex.GetType().Name})");
            return BadRequest(new ErrorResponse("Failed to update repository", ex.Message));
        }
    }

    [HttpPost("{repoUrl}/asset-rule-preview")]
    public async Task<ActionResult<AssetRulePreviewResponse>> PreviewAssetRule(string repoUrl, [FromBody] AssetRulePreviewRequest request)
    {
        RepoResponse? repo = await _service.GetRepoAsync(Uri.UnescapeDataString(repoUrl));
        if (repo == null) return NotFound(new ErrorResponse("Repository not found"));
        Github_Downloader_lib.Models.Repo preview = new()
        {
            Name = repo.Name, Url = repo.Url, AssetName = repo.AssetName, AssetRule = request.AssetRule
        };
        preview.ReplaceAssets(repo.AssetNames, []);
        return Ok(new AssetRulePreviewResponse(preview.AssetRuleMatches, preview.AssetSelectionStatus));
    }

    [HttpDelete("{repoUrl}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string repoUrl)
    {
        string decodedUrl = Uri.UnescapeDataString(repoUrl);
        bool deleted = await _service.DeleteRepoAsync(decodedUrl);
        if (!deleted)
            return NotFound(new ErrorResponse("Repository not found", $"No repository found with URL: {decodedUrl}"));
        
        return NoContent();
    }
}
