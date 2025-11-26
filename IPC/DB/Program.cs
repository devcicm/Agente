using IPC.DB.Models;
using IPC.DB.Options;
using IPC.DB.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.SingleLine = true;
});

builder.Services.Configure<DbConnectionOptions>(builder.Configuration.GetSection("Database"));
builder.Services.AddSingleton<IDatabaseExecutor>(sp =>
{
    var options = sp.GetRequiredService<IOptions<DbConnectionOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<SqlDatabaseExecutor>>();
    return options.Provider?.ToUpperInvariant() switch
    {
        "SQLSERVER" or null => new SqlDatabaseExecutor(options.ConnectionString, logger),
        _ => throw new NotSupportedException($"Proveedor '{options.Provider}' no soportado.")
    };
});

var app = builder.Build();
app.Logger.LogInformation("IPC DB service running. Provider: {Provider} | Connection: {Connection}",
    app.Configuration.GetSection("Database")["Provider"],
    app.Configuration.GetSection("Database")["ConnectionString"]);

app.MapGet("/ipc/db/health", () =>
{
    var payload = new
    {
        Message = "IPC DB service ready",
        Timestamp = DateTimeOffset.UtcNow
    };

    return Results.Ok(payload);
});

app.MapPost("/ipc/db/command", async (DbCommandRequest request, IDatabaseExecutor executor) =>
{
    if (string.IsNullOrWhiteSpace(request.Sql))
    {
        return Results.BadRequest(new { Error = "Debe proporcionar un comando SQL." });
    }

    var rows = await executor.ExecuteAsync(request.Sql, request.Parameters);
    return Results.Ok(new { Status = "OK", RowsAffected = rows });
});

app.MapPost("/ipc/db/query", async (DbQueryRequest request, IDatabaseExecutor executor) =>
{
    if (string.IsNullOrWhiteSpace(request.Sql))
    {
        return Results.BadRequest(new { Error = "Debe proporcionar una consulta SQL." });
    }

    var result = await executor.QueryAsync(request.Sql, request.Parameters);
    return Results.Ok(result);
});

app.Run();
