using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DynamicConfig.WebUI.Contracts;

namespace DynamicConfig.WebUI.Tests.Contracts;

/// <summary>
/// Shape-only guarantees of the HTTP contract: DataAnnotations reject garbage at the
/// door, the tri-state binds as intended, and server-owned fields are structurally
/// absent from the writable types.
/// </summary>
public class RequestContractTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static CreateConfigurationRequest BuildValidRequest(
        string name = "SiteName",
        string type = "string",
        string value = "soty.io",
        string applicationName = "SERVICE-A") => new()
    {
        Name = name,
        Type = type,
        Value = value,
        ApplicationName = applicationName,
    };

    private static List<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ValidRequest_PassesDataAnnotations()
    {
        Assert.Empty(Validate(BuildValidRequest()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrBlankName_FailsWithNameFieldDetail(string name)
    {
        var results = Validate(BuildValidRequest(name: name));

        var failure = Assert.Single(results);
        Assert.Contains(nameof(ConfigurationWriteRequest.Name), failure.MemberNames);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrBlankApplicationName_FailsWithFieldDetail(string applicationName)
    {
        var results = Validate(BuildValidRequest(applicationName: applicationName));

        var failure = Assert.Single(results);
        Assert.Contains(nameof(ConfigurationWriteRequest.ApplicationName), failure.MemberNames);
    }

    [Fact]
    public void BlankType_FailsWithFieldDetail_ButUnknownTypeNamePasses()
    {
        // Shape layer only checks presence — "decimal" is the SERVICE's rejection
        // (deep rule); duplicating it here would create the two-layer overlap we ban.
        Assert.NotEmpty(Validate(BuildValidRequest(type: "  ")));
        Assert.Empty(Validate(BuildValidRequest(type: "decimal")));
    }

    [Fact]
    public void OmittedIsActive_DeserializesAsNull()
    {
        // The binder end of the tri-state channel: absent JSON property → null →
        // service defaults to active. No hidden false sneaking in from bool's default.
        const string json = """{"name":"A","type":"string","value":"v","applicationName":"APP"}""";

        var request = JsonSerializer.Deserialize<CreateConfigurationRequest>(json, WebJsonOptions);

        Assert.Null(request!.IsActive);
    }

    [Fact]
    public void ExplicitIsActiveFalse_DeserializesAsFalse()
    {
        const string json = """{"name":"A","type":"string","value":"v","applicationName":"APP","isActive":false}""";

        var request = JsonSerializer.Deserialize<CreateConfigurationRequest>(json, WebJsonOptions);

        Assert.False(request!.IsActive);
    }

    [Theory]
    [InlineData(typeof(CreateConfigurationRequest))]
    [InlineData(typeof(UpdateConfigurationRequest))]
    public void WriteRequests_StructurallyCannotCarryServerOwnedFields(Type requestType)
    {
        // Enforced by type, not documentation: a client cannot set Id or
        // LastModifiedDate because no such property exists to bind to.
        Assert.Null(requestType.GetProperty("Id"));
        Assert.Null(requestType.GetProperty("LastModifiedDate"));
    }
}
