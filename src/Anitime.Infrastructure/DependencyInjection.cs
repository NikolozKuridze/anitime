using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Anitime.Application.Abstractions;
using Anitime.Infrastructure.Auth;
using Anitime.Infrastructure.Caching;
using Anitime.Infrastructure.Email;
using Anitime.Infrastructure.Messaging;
using Anitime.Infrastructure.Notifications;
using Anitime.Infrastructure.Options;
using Anitime.Infrastructure.Persistence;
using Anitime.Infrastructure.Storage;
using Anitime.Infrastructure.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Anitime.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<MinioOptions>(configuration.GetSection(MinioOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<ExerciseLibraryOptions>(configuration.GetSection(ExerciseLibraryOptions.SectionName));

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
            options.InstanceName = "anitime:";
        });
        services.AddSingleton<IDistributedCache>(provider => new ResilientDistributedCache(
            new RedisCache(provider.GetRequiredService<IOptions<RedisCacheOptions>>()),
            provider.GetRequiredService<ILogger<ResilientDistributedCache>>()));
        services.AddSingleton<ReminderDeliveryDeduplicator>();

        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, HttpUserContext>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IReminderPublisher, RabbitMqReminderPublisher>();
        services.AddSingleton<IPushNotificationSender, LoggingPushNotificationSender>();
        services.AddSingleton<IFileStorageService, MinioFileStorageService>();

        return services;
    }

    /// <summary>Resend when configured; otherwise a log sender in Development and a no-op that warns everywhere else.</summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);
        services.Configure<EmailOptions>(section);
        var options = section.Get<EmailOptions>() ?? new EmailOptions();

        if (options.IsResendConfigured)
        {
            services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
            {
                client.BaseAddress = new Uri(ResendEmailSender.BaseAddress);
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }
        else if (environment.IsDevelopment())
        {
            services.AddSingleton<IEmailSender, DevelopmentLogEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
        }

        services.AddSingleton<BackgroundEmailOutbox>();
        services.AddSingleton<IEmailOutbox>(provider => provider.GetRequiredService<BackgroundEmailOutbox>());
        services.AddHostedService(provider => provider.GetRequiredService<BackgroundEmailOutbox>());
        return services;
    }

    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var key = Encoding.UTF8.GetBytes(jwt.SigningKey);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub
                };
            });

        services.AddAuthorization();

        return services;
    }
}
