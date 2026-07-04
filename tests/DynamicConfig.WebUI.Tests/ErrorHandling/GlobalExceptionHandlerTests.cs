using System.Text.Json;
using DynamicConfig.WebUI.ErrorHandling;
using DynamicConfig.WebUI.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DynamicConfig.WebUI.Tests.ErrorHandling;

public class GlobalExceptionHandlerTests
{
    private const string RecordId = "65f1a2b3c4d5e6f7a8b9c0d1";

    // --- Mapping table -----------------------------------------------------------

    [Fact]
    public void Map_ValidationException_Produces400CarryingFieldName()
    {
        var exception = ConfigurationValidationException.UnsupportedType("decimal");

        var problem = GlobalExceptionHandler.MapToProblemDetails(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal(exception.Message, problem.Detail);
        // 4.3's form attaches the error to the right input using this extension.
        Assert.Equal("Type", problem.Extensions[GlobalExceptionHandler.FieldNameExtensionKey]);
    }

    // Audit Block 2 pin: the fieldName guarantee holds for EVERY factory, not just
    // UnsupportedType — the exception's private constructor forces all instances
    // through a factory, and each factory names the failing record field.
    public static TheoryData<ConfigurationValidationException, string> EveryValidationFactory => new()
    {
        { ConfigurationValidationException.RequiredFieldMissing("Name"), "Name" },
        { ConfigurationValidationException.MalformedId("not-an-objectid"), "Id" },
        { ConfigurationValidationException.UnsupportedType("banana"), "Type" },
        { ConfigurationValidationException.ValueTypeMismatch("abc", "int"), "Value" },
    };

    [Theory]
    [MemberData(nameof(EveryValidationFactory))]
    public void Map_EveryValidationFactory_Produces400CarryingItsFieldName(
        ConfigurationValidationException exception,
        string expectedFieldName)
    {
        var problem = GlobalExceptionHandler.MapToProblemDetails(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal(expectedFieldName, problem.Extensions[GlobalExceptionHandler.FieldNameExtensionKey]);
    }

    [Fact]
    public void Map_RecordNotFoundException_Produces404CarryingRecordId()
    {
        var exception = new ConfigurationRecordNotFoundException(RecordId);

        var problem = GlobalExceptionHandler.MapToProblemDetails(exception);

        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal(exception.Message, problem.Detail);
        Assert.Equal(RecordId, problem.Extensions[GlobalExceptionHandler.RecordIdExtensionKey]);
    }

    [Fact]
    public void Map_UnexpectedException_Produces500LeakingNothing()
    {
        var exception = new InvalidOperationException("secret internal state: connection string xyz");

        var problem = GlobalExceptionHandler.MapToProblemDetails(exception);

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        // Neither the message nor the type of the internal exception may reach a client.
        Assert.DoesNotContain("secret", problem.Detail ?? string.Empty);
        Assert.DoesNotContain(nameof(InvalidOperationException), JsonSerializer.Serialize(problem));
    }

    // --- HTTP pipeline behavior -----------------------------------------------------

    [Fact]
    public async Task TryHandleAsync_WritesProblemDetailsJsonWithMatchingStatusCode()
    {
        var handler = new GlobalExceptionHandler(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new ConfigurationRecordNotFoundException(RecordId),
            CancellationToken.None);

        Assert.True(handled); // false would re-throw and bypass the ProblemDetails shape
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);

        httpContext.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body);
        Assert.Equal(StatusCodes.Status404NotFound, body!.Status);
    }
}
