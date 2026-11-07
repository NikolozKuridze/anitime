using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Anitime.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Anitime.Api.Tests;

/// <summary>Real API + real PostgreSQL (Docker required). Redis is swapped for an in-memory cache.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private WebApplicationFactory<Program> _factory = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Program.cs reads configuration while building the host, so environment variables are the reliable override.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDistributedCache>();
                services.AddSingleton<IDistributedCache>(
                    new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
            });
        });
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

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
