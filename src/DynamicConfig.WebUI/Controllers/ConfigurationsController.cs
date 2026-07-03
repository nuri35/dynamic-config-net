using DynamicConfig.WebUI.Contracts;
using DynamicConfig.WebUI.Services;
using Microsoft.AspNetCore.Mvc;

namespace DynamicConfig.WebUI.Controllers;

/// <summary>
/// Admin REST surface over configuration records. Deliberately thin: every action is
/// bind → service call → wrap. Business rules live in
/// <see cref="IConfigurationAdminService"/> (4.1), error translation lives in the
/// exception handler middleware — an action never catches, never validates deeply.
/// </summary>
[ApiController]
[Route("api/configurations")]
[Produces("application/json")]
public sealed class ConfigurationsController : ControllerBase
{
    private readonly IConfigurationAdminService _configurationAdminService;

    public ConfigurationsController(IConfigurationAdminService configurationAdminService)
    {
        _configurationAdminService = configurationAdminService;
    }

    /// <summary>Lists every configuration record — all applications, inactive included.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ConfigurationResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ConfigurationResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var records = await _configurationAdminService.GetAllAsync(cancellationToken);
        return Ok(records.Select(ConfigurationResponse.FromRecord));
    }

    /// <summary>Returns the configuration record with the given id.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConfigurationResponse>> GetById(string id, CancellationToken cancellationToken)
    {
        var record = await _configurationAdminService.GetByIdAsync(id, cancellationToken);
        return Ok(ConfigurationResponse.FromRecord(record));
    }

    /// <summary>Creates a configuration record. Omitting <c>isActive</c> creates it active.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ConfigurationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConfigurationResponse>> Create(
        CreateConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _configurationAdminService.CreateAsync(request.ToRecord(), request.IsActive, cancellationToken);
        var response = ConfigurationResponse.FromRecord(created);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    /// <summary>Replaces the configuration record at the given id. Never upserts.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConfigurationResponse>> Update(
        string id,
        UpdateConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _configurationAdminService.UpdateAsync(request.ToRecord(id), cancellationToken);
        return Ok(ConfigurationResponse.FromRecord(updated));
    }
}
