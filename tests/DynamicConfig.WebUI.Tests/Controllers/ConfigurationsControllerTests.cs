using DynamicConfig.Library.Models;
using DynamicConfig.WebUI.Contracts;
using DynamicConfig.WebUI.Controllers;
using DynamicConfig.WebUI.Exceptions;
using DynamicConfig.WebUI.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;

namespace DynamicConfig.WebUI.Tests.Controllers;

/// <summary>
/// Shell tests only: right service method, right arguments, right status wrapping.
/// Business rules are 4.1's tests; error translation is the handler's tests.
/// </summary>
public class ConfigurationsControllerTests
{
    private const string ExistingId = "65f1a2b3c4d5e6f7a8b9c0d1";

    private readonly FakeConfigurationAdminService _service = new();
    private readonly ConfigurationsController _controller;

    public ConfigurationsControllerTests()
    {
        _controller = new ConfigurationsController(_service);
    }

    private static ConfigurationRecord BuildRecord(string id = ExistingId, bool isActive = true) => new()
    {
        Id = id,
        Name = "SiteName",
        Type = "string",
        Value = "soty.io",
        IsActive = isActive,
        ApplicationName = "SERVICE-A",
        LastModifiedDate = FakeConfigurationAdminService.StampedDate,
    };

    private static CreateConfigurationRequest BuildCreateRequest(bool? isActive = null) => new()
    {
        Name = "MaxItemCount",
        Type = "int",
        Value = "50",
        ApplicationName = "SERVICE-B",
        IsActive = isActive,
    };

    // --- GET /api/configurations -----------------------------------------------

    [Fact]
    public async Task GetAll_ReturnsOkWithEveryRecordMapped()
    {
        _service.Seed(BuildRecord(), BuildRecord(id: "65f1a2b3c4d5e6f7a8b9c0d2", isActive: false));

        var result = await _controller.GetAll(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var responses = Assert.IsAssignableFrom<IEnumerable<ConfigurationResponse>>(ok.Value).ToList();
        Assert.Equal(2, responses.Count);
        Assert.Contains(responses, response => !response.IsActive); // admin view: inactive included
    }

    // --- GET /api/configurations/{id} -------------------------------------------

    [Fact]
    public async Task GetById_PassesRouteIdAndReturnsOkWithMappedRecord()
    {
        _service.Seed(BuildRecord());

        var result = await _controller.GetById(ExistingId, CancellationToken.None);

        Assert.Equal(ExistingId, _service.LastRequestedId);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ConfigurationResponse>(ok.Value);
        Assert.Equal(ExistingId, response.Id);
        Assert.Equal(FakeConfigurationAdminService.StampedDate, response.LastModifiedDate);
    }

    [Fact]
    public async Task GetById_ServiceThrowsNotFound_ExceptionPropagatesUncaught()
    {
        // The action must not catch — translation is the middleware's job. A swallowed
        // exception here would turn every 404 into a 200.
        _service.ExceptionToThrow = new ConfigurationRecordNotFoundException(ExistingId);

        await Assert.ThrowsAsync<ConfigurationRecordNotFoundException>(
            () => _controller.GetById(ExistingId, CancellationToken.None));
    }

    // --- POST /api/configurations ------------------------------------------------

    [Fact]
    public async Task Create_Returns201PointingAtGetByIdWithServerOwnedFields()
    {
        var result = await _controller.Create(BuildCreateRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(ConfigurationsController.GetById), created.ActionName);
        Assert.Equal(FakeConfigurationAdminService.GeneratedId, created.RouteValues!["id"]);
        var response = Assert.IsType<ConfigurationResponse>(created.Value);
        Assert.Equal(FakeConfigurationAdminService.GeneratedId, response.Id);
    }

    [Fact]
    public async Task Create_OmittedIsActive_FlowsThroughAsNull()
    {
        // The DTO's nullable bool is the HTTP end of 4.1's tri-state channel — the
        // controller must forward null untouched so the SERVICE decides the default.
        await _controller.Create(BuildCreateRequest(isActive: null), CancellationToken.None);

        Assert.Null(_service.LastCreateIsActive);
    }

    [Fact]
    public async Task Create_ExplicitIsActiveFalse_FlowsThroughAsFalse()
    {
        await _controller.Create(BuildCreateRequest(isActive: false), CancellationToken.None);

        Assert.False(_service.LastCreateIsActive);
    }

    [Fact]
    public async Task Create_MapsRequestFieldsOntoRecord()
    {
        await _controller.Create(BuildCreateRequest(), CancellationToken.None);

        var record = _service.LastCreatedRecord!;
        Assert.Equal("MaxItemCount", record.Name);
        Assert.Equal("int", record.Type);
        Assert.Equal("50", record.Value);
        Assert.Equal("SERVICE-B", record.ApplicationName);
    }

    // --- PUT /api/configurations/{id} ---------------------------------------------

    [Fact]
    public async Task Update_TargetsRouteIdAndReturnsOk()
    {
        var request = new UpdateConfigurationRequest
        {
            Name = "SiteName",
            Type = "string",
            Value = "soty.com",
            ApplicationName = "SERVICE-A",
        };

        var result = await _controller.Update(ExistingId, request, CancellationToken.None);

        // The body cannot carry an id; the route is the only channel.
        Assert.Equal(ExistingId, _service.LastUpdatedRecord!.Id);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ConfigurationResponse>(ok.Value);
        Assert.Equal("soty.com", response.Value);
    }

    [Fact]
    public async Task Update_ServiceThrowsNotFound_ExceptionPropagatesUncaught()
    {
        _service.ExceptionToThrow = new ConfigurationRecordNotFoundException(ExistingId);
        var request = new UpdateConfigurationRequest
        {
            Name = "SiteName",
            Type = "string",
            Value = "soty.com",
            ApplicationName = "SERVICE-A",
        };

        await Assert.ThrowsAsync<ConfigurationRecordNotFoundException>(
            () => _controller.Update(ExistingId, request, CancellationToken.None));
    }
}
