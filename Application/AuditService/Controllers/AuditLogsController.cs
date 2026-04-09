using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditService.Controllers;

[Authorize(Policy = "ServiceOnly")]
[ApiController]
[Route("audit-logs")]
public sealed class AuditLogsController : ControllerBase
{
    private static readonly IReadOnlyList<object> Logs =
    [
        new { Id = 1, Event = "LOGIN_SUCCESS",  Subject = "testuser",        Resource = "gateway-client",      IpAddress = "192.168.1.10", Timestamp = "2025-11-01T08:00:00Z" },
        new { Id = 2, Event = "LOGIN_FAILED",   Subject = "unknown",         Resource = "gateway-client",      IpAddress = "10.0.0.99",    Timestamp = "2025-11-01T08:01:23Z" },
        new { Id = 3, Event = "TOKEN_ISSUED",   Subject = "testuser",        Resource = "sentinel-service",    IpAddress = "192.168.1.10", Timestamp = "2025-11-01T08:00:05Z" },
        new { Id = 4, Event = "ACCESS_DENIED",  Subject = "unknown",         Resource = "audit-service",       IpAddress = "10.0.0.99",    Timestamp = "2025-11-01T08:01:30Z" },
        new { Id = 5, Event = "TOKEN_EXPIRED",  Subject = "testuser",        Resource = "gateway-client",      IpAddress = "192.168.1.10", Timestamp = "2025-11-01T08:05:00Z" }
    ];

    [HttpGet]
    public IActionResult GetAll() => Ok(Logs);

    [HttpGet("{id:int}")]
    public IActionResult GetById(int id)
    {
        var log = Logs.FirstOrDefault(l =>
            (int)l.GetType().GetProperty("Id")!.GetValue(l)! == id);

        return log is null ? NotFound() : Ok(log);
    }
}