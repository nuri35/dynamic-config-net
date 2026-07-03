using DynamicConfig.Library.Storage.Mongo;
using DynamicConfig.WebUI.Services;
using DynamicConfig.WebUI.Storage;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Composition root for the admin backend (Phase 4.1). The HTTP surface arrives in
// Phase 4.2 — until then the app only wires its dependencies.
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

var app = builder.Build();

app.MapGet("/", () => "DynamicConfig WebUI — REST API and frontend arrive in Phase 4.2/4.3.");

app.Run();
