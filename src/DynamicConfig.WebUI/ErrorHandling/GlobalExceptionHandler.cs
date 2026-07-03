using DynamicConfig.WebUI.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DynamicConfig.WebUI.ErrorHandling;

/// <summary>
/// The single place exceptions become HTTP responses (the NestJS exception-filter
/// counterpart, as .NET 8's <see cref="IExceptionHandler"/>). Controllers never
/// catch; everything surfaces here and leaves as RFC 7807 ProblemDetails.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    /// <summary>ProblemDetails extension carrying the failing record field on 400s.</summary>
    internal const string FieldNameExtensionKey = "fieldName";

    /// <summary>ProblemDetails extension carrying the missing record id on 404s.</summary>
    internal const string RecordIdExtensionKey = "recordId";

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = MapToProblemDetails(exception);

        // Known business exceptions are expected traffic; only genuinely unexpected
        // failures are error-logged (with full detail — logs are trusted, bodies are not).
        if (problemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Path}", httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problemDetails.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true; // handled: nothing re-throws past the ProblemDetails shape
    }

    internal static ProblemDetails MapToProblemDetails(Exception exception) => exception switch
    {
        ConfigurationValidationException validation => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed.",
            Detail = validation.Message,
            Extensions = { [FieldNameExtensionKey] = validation.FieldName },
        },
        ConfigurationRecordNotFoundException notFound => new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Configuration record not found.",
            Detail = notFound.Message,
            Extensions = { [RecordIdExtensionKey] = notFound.RecordId },
        },
        // Deliberately no Detail and no exception type: internals never reach a client.
        _ => new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
        },
    };
}
