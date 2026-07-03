using DynamicConfig.Library.Storage.Mongo;
using DynamicConfig.WebUI.ErrorHandling;
using DynamicConfig.WebUI.Services;
using DynamicConfig.WebUI.Storage;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// --- Storage & domain services -------------------------------------------------
const string mongoConnectionStringName = "Mongo";
var mongoConnectionString = builder.Configuration.GetConnectionString(mongoConnectionStringName)
    ?? throw new InvalidOperationException(
        $"Connection string '{mongoConnectionStringName}' is missing from configuration.");

// MongoClient is thread-safe and pools connections internally — one per process is
// the driver's own guidance (similar to reusing a single Mongoose connection in Node).
builder.Services.AddSingleton<IMongoDatabase>(_ =>
{
    var mongoUrl = MongoUrl.Create(mongoConnectionString);
    var databaseName = string.IsNullOrWhiteSpace(mongoUrl.DatabaseName)
        ? MongoConfigurationStorageDefaults.DatabaseName
        : mongoUrl.DatabaseName;
    return new MongoClient(mongoUrl).GetDatabase(databaseName);
});
builder.Services.AddSingleton<IConfigurationAdminRepository, MongoConfigurationAdminRepository>();
builder.Services.AddSingleton<IConfigurationAdminService, ConfigurationAdminService>();

// --- HTTP surface ----------------------------------------------------------------
builder.Services.AddControllers();

// Exceptions become RFC 7807 ProblemDetails in exactly one place (the NestJS
// exception-filter counterpart); controllers stay catch-free.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DynamicConfig Admin API",
        Version = "v1",
        Description = "Manages configuration records served to consuming services by DynamicConfig.Library. "
            + "No authentication by design — see the README's scope note.",
    });

    // XML comments from the build feed endpoint/DTO summaries into the Swagger UI.
    var xmlDocumentationPath = Path.Combine(
        AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    options.IncludeXmlComments(xmlDocumentationPath);
});

var app = builder.Build();

app.UseExceptionHandler();

// Swagger stays on in every environment on purpose: this is an internal admin tool
// and the case reviewer must be able to exercise the API from a container (which
// runs as Production by default) without extra configuration.
app.UseSwagger();
app.UseSwaggerUI();

// The admin frontend (4.3): plain static files from wwwroot — no framework, no
// build pipeline. UseDefaultFiles rewrites "/" to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
