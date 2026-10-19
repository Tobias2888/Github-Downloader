using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Microsoft.AspNetCore.Mvc;

namespace Github_Downloader_Web.Controllers;

[ApiController]
[Route("api/history")]
public sealed class HistoryController : ControllerBase
{
    [HttpGet]
    public ActionResult<HistoryPage> Get(string? repository = null, DateTimeOffset? from = null,
        DateTimeOffset? to = null, HistoryStatus? status = null, HistoryAction? action = null,
        Guid? batchId = null, int offset = 0, int limit = 200, bool includeSteps = false, bool includeSkipped = false)
    {
        try
        {
            return Ok(OperationHistory.Store.Query(new(repository, from, to, status, action, batchId, offset, limit, includeSteps, includeSkipped)));
        }
        catch (ArgumentException) { return BadRequest(new { error = "Invalid history filters or page size." }); }
        catch (IOException) { return StatusCode(500, new { error = "History could not be read. Its file has been preserved." }); }
        catch (UnauthorizedAccessException) { return StatusCode(500, new { error = "History access was denied." }); }
    }

    [HttpDelete]
    public ActionResult Clear([FromQuery] bool confirm = false)
    {
        if (!confirm) return BadRequest(new { error = "Confirm clearing history with confirm=true." });
        try { return Ok(new { removed = OperationHistory.Store.Clear() }); }
        catch (IOException) { return StatusCode(500, new { error = "History could not be cleared." }); }
        catch (UnauthorizedAccessException) { return StatusCode(500, new { error = "History access was denied." }); }
    }
}
