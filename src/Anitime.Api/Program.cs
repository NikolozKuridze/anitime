using System.Text.Json.Serialization;
using Anitime.Application.Validation;
using Anitime.Api.Infrastructure;
using Anitime.Api.Swagger;
using Anitime.Contracts;
using Anitime.Infrastructure;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Npgsql;

const string PlaceholderSigningKeyPrefix = "ChangeThis";
const int MaxRequestJsonDepth = 64;
const int MaxRequestValidationDepth = 256;

var builder = WebApplication.CreateBuilder(args);
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddEmail(builder.Configuration, builder.Environment);
builder.Services.AddValidatorsFromAssemblyContaining<SignUpRequestValidator>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientOrigins", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});
// Rich-text notes nest: the MVC default depth of 32 (about 9 list levels) turned valid documents into HTTP 500.
builder.Services.AddControllers(options =>
    {
        options.MaxValidationDepth = MaxRequestValidationDepth;
        // Also bounds the ModelState key recorded for a JSON error, so over-deep bodies return 400 instead of throwing.
        options.MaxModelBindingRecursionDepth = MaxRequestValidationDepth;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.MaxDepth = MaxRequestJsonDepth;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new NulStrippingStringConverter());
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    });
builder.Services.AddAuthRateLimiting();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Only the scheme is taken from the proxy, so it can't be used to spoof a client IP or host.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Anitime API",
        Version = "v1",
        Description = "Habit challenge, task tracking, reminders, profile customization, and dashboard API."
    });
    options.SchemaFilter<ExamplesSchemaFilter>();
    options.OperationFilter<DefaultResponsesOperationFilter>();
    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = JwtBearerDefaults.AuthenticationScheme
                }
            },
            []
        }
    });
});

var app = builder.Build();

if (app.Environment.IsProduction() &&
    app.Configuration["Jwt:SigningKey"]?.StartsWith(PlaceholderSigningKeyPrefix, StringComparison.Ordinal) != false)
{
    throw new InvalidOperationException("Jwt:SigningKey must be set to a real secret in production.");
}

using (var scope = app.Services.CreateScope())
{
    await EnsureDatabaseReadyAsync(scope.ServiceProvider, app.Logger, app.Lifetime.ApplicationStopping);
    var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await ExerciseCatalogSeeder.SeedAsync(seedDb, app.Environment.ContentRootPath, app.Logger, app.Lifetime.ApplicationStopping);
    await WorkoutCatalogSeeder.SeedAsync(seedDb, app.Environment.ContentRootPath, app.Logger, app.Lifetime.ApplicationStopping);
}

app.UseForwardedHeaders();
app.UseExceptionHandler(errorApp => errorApp.Run(context =>
{
    // A body past the size limit (e.g. chunked, with no Content-Length to check up front) surfaces as this exception.
    if (context.Features.Get<IExceptionHandlerFeature>()?.Error is BadHttpRequestException
        { StatusCode: StatusCodes.Status413PayloadTooLarge })
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return context.Response.WriteAsJsonAsync(new ApiErrorResponse(
            "payload_too_large",
            "The request is too large.",
            null,
            context.TraceIdentifier));
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    return context.Response.WriteAsJsonAsync(new ApiErrorResponse(
        "internal_error",
        "An unexpected error occurred.",
        null,
        context.TraceIdentifier));
}));
app.Use((context, next) =>
{
    var hasNul = context.Request.Path.Value?.Contains('\0') == true ||
        context.Request.QueryString.Value?.Contains("%00", StringComparison.Ordinal) == true;
    if (!hasNul)
    {
        return next();
    }

    context.Response.StatusCode = StatusCodes.Status400BadRequest;
    return context.Response.WriteAsJsonAsync(new ApiErrorResponse(
        "invalid_request",
        "The URL contains an invalid character.",
        null,
        context.TraceIdentifier));
});
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("ClientOrigins");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

static async Task EnsureDatabaseReadyAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
{
    const int maxAttempts = 20;
    const long schemaLockKey = 42_017_171;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.Database.OpenConnectionAsync(cancellationToken);

            await using var lockCommand = db.Database.GetDbConnection().CreateCommand();
            lockCommand.CommandText = $"SELECT pg_advisory_lock({schemaLockKey})";
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);

            try
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await using var unlockCommand = db.Database.GetDbConnection().CreateCommand();
                unlockCommand.CommandText = $"SELECT pg_advisory_unlock({schemaLockKey})";
                await unlockCommand.ExecuteNonQueryAsync(cancellationToken);
                await db.Database.CloseConnectionAsync();
            }

            return;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            if (attempt == maxAttempts)
            {
                throw;
            }

            logger.LogWarning(ex, "Database is not ready yet. Retry {Attempt}/{MaxAttempts}", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }
}

// Exposes the implicit entry point to WebApplicationFactory in Anitime.Api.Tests.
public partial class Program;
