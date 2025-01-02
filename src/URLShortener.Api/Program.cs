using Microsoft.AspNetCore.Mvc;
using OpenTelemetry;
using URLShortener.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDataSource("url-shortener");
builder.AddRedisDistributedCache("redis");
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("UrlShortener.Api"));

#pragma warning disable EXTEXP0018 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
builder.Services.AddHybridCache();
#pragma warning restore EXTEXP0018 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

builder.Services.AddOpenApi();

builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddScoped<UrlShorteningService>();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "OpenAPI V1"));
}

#region Endpoints

app.MapPost("shorten", async ([FromBody] string url, UrlShorteningService svc, CancellationToken cancellationToken) =>
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out _))
    {
        return Results.BadRequest("Invalid URL format.");
    }

    var shortCode = await svc.ShortenUrlAsync(url, cancellationToken);
    return Results.Ok(new { shortCode });
});

app.MapGet("{shortCode}", async (string shortCode, UrlShorteningService svc, CancellationToken cancellationToken) =>
{
    var originalUrl = await svc.GetOriginalUrlAsync(shortCode, cancellationToken);
    if (originalUrl is null)
    {
        return Results.NotFound();
    }
    return Results.Redirect(originalUrl);
});

app.MapGet("urls", async (UrlShorteningService svc, CancellationToken cancellationToken) =>
{
    var urls = await svc.GetAllUrlsAsync(cancellationToken);
    return Results.Ok(urls);
});

#endregion Endpoints

app.UseHttpsRedirection();
app.Run();