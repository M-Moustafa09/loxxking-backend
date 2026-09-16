using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Domain.Entities.PageConfigurations;
using loxxking_backend_clean.Shared;

namespace loxxking_backend_clean.Api.Controllers;

[ApiController]
[Route("api/page-configurations")]
public class PageConfigurationsController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public PageConfigurationsController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var configs = await _context.PageConfigurations
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Key, c => c.ConfigJson, ct);

        return Ok(ApiResponse<Dictionary<string, string>>.Ok(configs));
    }

    [HttpGet("{key}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByKey(string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return BadRequest("Key is required");

        var config = await _context.PageConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == key, ct);

        if (config == null)
            return NotFound(ApiResponse<string>.Fail($"Configuration for key '{key}' not found."));

        return Ok(ApiResponse<PageConfigurationDto>.Ok(new PageConfigurationDto
        {
            Key = config.Key,
            ConfigJson = config.ConfigJson,
            UpdatedAt = config.UpdatedAt
        }));
    }

    [HttpPut("{key}")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> Upsert(string key, [FromBody] SavePageConfigRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return BadRequest("Key is required");
        var json = request?.ConfigJson ?? string.Empty;

        var existing = await _context.PageConfigurations.FirstOrDefaultAsync(c => c.Key == key, ct);
        if (existing == null)
        {
            _context.PageConfigurations.Add(new PageConfiguration
            {
                Key = key,
                ConfigJson = json,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.ConfigJson = json;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("batch")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> BatchUpsert([FromBody] BatchSavePageConfigRequest request, CancellationToken ct)
    {
        if (request?.Configs == null || request.Configs.Count == 0)
            return BadRequest("No configurations provided");

        var keys = request.Configs.Keys.ToList();
        var existingList = await _context.PageConfigurations
            .Where(c => keys.Contains(c.Key))
            .ToListAsync(ct);

        var existingDict = existingList.ToDictionary(c => c.Key);

        foreach (var kv in request.Configs)
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;

            if (existingDict.TryGetValue(kv.Key, out var existing))
            {
                existing.ConfigJson = kv.Value ?? string.Empty;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.PageConfigurations.Add(new PageConfiguration
                {
                    Key = kv.Key,
                    ConfigJson = kv.Value ?? string.Empty,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync(ct);
        return Ok(ApiResponse<int>.Ok(request.Configs.Count));
    }
}

public class SavePageConfigRequest
{
    public string ConfigJson { get; set; } = string.Empty;
}

public class BatchSavePageConfigRequest
{
    public Dictionary<string, string> ConfigConfigs { get => Configs; set => Configs = value; }
    public Dictionary<string, string> Configs { get; set; } = new();
}

public class PageConfigurationDto
{
    public string Key { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
