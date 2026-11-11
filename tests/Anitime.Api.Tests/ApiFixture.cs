using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Anitime.Application.Abstractions;
using Anitime.Infrastructure.Caching;
using Anitime.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Anitime.Api.Tests;

/// <summary>Real API + real PostgreSQL (Docker required). Redis is swapped for an in-memory cache.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private WebApplicationFactory<Program> _factory = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Program.cs reads configuration while building the host, so environment variables are the reliable override.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        // Tests share one client IP; only the dedicated rate-limit test uses a low limit.
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitLimit", "1000000");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDistributedCache>();
                services.AddSingleton<IDistributedCache>(
                    new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService, InMemoryFileStorage>();
                services.AddSingleton<ILoggerProvider>(new CollectingLoggerProvider(Logs));
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
            });
        });
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public ConcurrentBag<string> Logs { get; } = [];

    public CapturingEmailSender Emails { get; } = new();

    public HttpClient AnonymousClient() => _factory.CreateClient();

    public async Task<HttpClient> NewUserClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = $"user-{Guid.NewGuid():N}@example.com",
            password = "Test-Password-123",
            name = "Test User",
            gender = "Male"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Runs work against a fresh DbContext, independent of any HTTP request.</summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> work)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    /// <summary>A client whose cache always fails, to exercise the unhandled-exception path.</summary>
    public HttpClient ClientWithFailingCache(string bearerToken)
    {
        var client = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, FailingCache>();
        })).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    /// <summary>A client whose cache fails behind the production resilience wrapper, as during a Redis outage.</summary>
    public HttpClient ClientWithCacheOutage(string bearerToken)
    {
        var client = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(provider => new ResilientDistributedCache(
                new FailingCache(), provider.GetRequiredService<ILogger<ResilientDistributedCache>>()));
        })).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    public HttpClient ClientWithAuthRateLimit(int permitLimit) =>
        _factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:AuthPermitLimit", permitLimit.ToString()))
            .CreateClient();

    public sealed class CapturingEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> _sent = new();

        public IReadOnlyCollection<EmailMessage> Sent => _sent;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            _sent.Enqueue(message);
            return Task.CompletedTask;
        }

        /// <summary>Delivery runs in the background, so wait for it.</summary>
        public async Task<EmailMessage> WaitForAsync(string to, int count = 1)
        {
            for (var i = 0; i < 100; i++)
            {
                var matches = _sent.Where(x => x.To == to).ToList();
                if (matches.Count >= count)
                {
                    return matches[count - 1];
                }

                await Task.Delay(50);
            }

            throw new TimeoutException($"No email #{count} was sent.");
        }
    }

    private sealed class InMemoryFileStorage : IFileStorageService
    {
        private readonly ConcurrentDictionary<string, byte[]> _objects = new();

        public async Task<string> UploadAsync(string objectName, Stream stream, string contentType, CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            _objects[objectName] = memory.ToArray();
            return objectName;
        }

        public Task<Stream?> OpenReadAsync(string? objectName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(objectName is not null && _objects.TryGetValue(objectName, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string? objectName, CancellationToken cancellationToken)
        {
            if (objectName is not null)
            {
                _objects.TryRemove(objectName, out _);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CollectingLoggerProvider(ConcurrentBag<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CollectingLogger(logs);
        public void Dispose() { }

        private sealed class CollectingLogger(ConcurrentBag<string> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Add(formatter(state, exception));
        }
    }

    private sealed class FailingCache : IDistributedCache
    {
        private static InvalidOperationException Failure() => new("Simulated cache outage.");
        public byte[]? Get(string key) => throw Failure();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Failure();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Failure();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Failure();
        public void Refresh(string key) => throw Failure();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Failure();
        public void Remove(string key) => throw Failure();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Failure();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
