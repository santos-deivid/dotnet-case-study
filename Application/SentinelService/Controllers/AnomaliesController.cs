using Microsoft.AspNetCore.Mvc;

namespace SentinelService.Controllers;

[ApiController]
[Route("anomalies")]
public sealed class AnomaliesController : ControllerBase
{
    private static readonly IReadOnlyList<object> Anomalies =
    [
        new { Id = 1, Code = "ANM-001", Description = "Unauthorized token replay detected", Severity = "Critical", DetectedAt = "2025-11-01T03:14:00Z" },
        new { Id = 2, Code = "ANM-002", Description = "Service registered without valid mTLS certificate", Severity = "High", DetectedAt = "2025-11-02T17:42:00Z" },
        new { Id = 3, Code = "ANM-003", Description = "JWT issued with overly broad scopes", Severity = "Medium", DetectedAt = "2025-11-03T09:05:00Z" }
    ];

    [HttpGet]
    public IActionResult GetAll() => Ok(Anomalies);

    [HttpGet("{id:int}")]
    public IActionResult GetById(int id)
    {
        var anomaly = Anomalies.FirstOrDefault(a =>
            (int)a.GetType().GetProperty("Id")!.GetValue(a)! == id);

        return anomaly is null ? NotFound() : Ok(anomaly);
    }
}