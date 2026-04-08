using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelService.Services;

namespace SentinelService.Controllers;

[Authorize]
[ApiController]
[Route("anomalies")]
public sealed class AnomaliesController(
    KeycloakTokenService tokenService,
    IHttpClientFactory httpClientFactory,
    ConsulDiscoveryService discoveryService)
    : ControllerBase
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
    
    [HttpGet("report")]
    public async Task<IActionResult> GetReport()
    {
        // 1. Descobrir endereço do AuditService via Consul
        var auditServiceUrl = await discoveryService.GetServiceUrlAsync("audit-service");

        // 2. Obter token do Keycloak via Client Credentials
        var token = await tokenService.GetAccessTokenAsync();

        // 3. Chamar AuditService via mTLS com o token
        var client = httpClientFactory.CreateClient("audit-service");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"{auditServiceUrl}/audit-logs");
        response.EnsureSuccessStatusCode();

        var auditLogs = await response.Content.ReadFromJsonAsync<JsonElement>();

        return Ok(new
        {
            GeneratedAt = DateTime.UtcNow,
            Anomalies,
            AuditLogs = auditLogs
        });
    }
}