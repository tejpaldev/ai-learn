using Microsoft.AspNetCore.Mvc;
using RagPipeline.Services;

namespace RagPipeline.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly RagEngine _ragEngine;
    private readonly ICacheService? _cacheService;

    public HealthController(RagEngine ragEngine, ICacheService? cacheService = null)
    {
        _ragEngine = ragEngine;
        _cacheService = cacheService;
    }

    /// <summary>
    /// Basic health check endpoint
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<HealthCheckResponse> Get()
    {
        return Ok(new HealthCheckResponse
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Version = "1.0.0"
        });
    }

    /// <summary>
    /// Detailed health check with component status
    /// </summary>
    [HttpGet("detailed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DetailedHealthCheckResponse>> GetDetailed()
    {
        var response = new DetailedHealthCheckResponse
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Version = "1.0.0"
        };

        // Check vector store
        try
        {
            var count = _ragEngine.GetIndexedChunksCount();
            response.Components["VectorStore"] = new ComponentHealth
            {
                Status = "Healthy",
                Details = new Dictionary<string, object>
                {
                    ["indexed_chunks"] = count
                }
            };
        }
        catch (Exception ex)
        {
            response.Components["VectorStore"] = new ComponentHealth
            {
                Status = "Unhealthy",
                Error = ex.Message
            };
            response.Status = "Degraded";
        }

        // Check cache
        if (_cacheService != null)
        {
            try
            {
                var cacheStats = await _cacheService.GetStatsAsync();
                response.Components["Cache"] = new ComponentHealth
                {
                    Status = "Healthy",
                    Details = new Dictionary<string, object>
                    {
                        ["hit_rate"] = cacheStats.HitRate,
                        ["current_size"] = cacheStats.CurrentSize,
                        ["max_size"] = cacheStats.MaxSize
                    }
                };
            }
            catch (Exception ex)
            {
                response.Components["Cache"] = new ComponentHealth
                {
                    Status = "Unhealthy",
                    Error = ex.Message
                };
                response.Status = "Degraded";
            }
        }

        return Ok(response);
    }

    /// <summary>
    /// Readiness probe for Kubernetes
    /// </summary>
    [HttpGet("ready")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult Ready()
    {
        // Check if system is ready to accept requests
        try
        {
            _ = _ragEngine.GetIndexedChunksCount();
            return Ok(new { status = "Ready" });
        }
        catch
        {
            return StatusCode(503, new { status = "Not Ready" });
        }
    }

    /// <summary>
    /// Liveness probe for Kubernetes
    /// </summary>
    [HttpGet("live")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Live()
    {
        return Ok(new { status = "Alive" });
    }
}

public class HealthCheckResponse
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Version { get; set; } = string.Empty;
}

public class DetailedHealthCheckResponse : HealthCheckResponse
{
    public Dictionary<string, ComponentHealth> Components { get; set; } = new();
}

public class ComponentHealth
{
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object>? Details { get; set; }
    public string? Error { get; set; }
}
