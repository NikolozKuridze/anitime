using System.Net;
using System.Text.Json;
using Anitime.Application.Abstractions;
using Anitime.Application.Rules;
using Anitime.Infrastructure.Email;
using Anitime.Infrastructure.Caching;
using Anitime.Infrastructure.Notifications;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anitime.Api.Tests;

public sealed class InfrastructureUnitTests
{
    private static MemoryDistributedCache MemoryCache() => new(Options.Create(new MemoryDistributedCacheOptions()));

    private sealed class DownCache : IDistributedCache
    {
        private static InvalidOperationException Down() => new("Redis is down.");
        public byte[]? Get(string key) => throw Down();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();
        public void Refresh(string key) => throw Down();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();
        public void Remove(string key) => throw Down();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
    }

    private sealed class CancelledCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new OperationCanceledException();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new OperationCanceledException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new OperationCanceledException();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new OperationCanceledException();
        public void Refresh(string key) => throw new OperationCanceledException();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new OperationCanceledException();
        public void Remove(string key) => throw new OperationCanceledException();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new OperationCanceledException();
    }

    [Fact]
    public async Task ResilientCache_DuringOutage_ReadsAsMiss_AndWritesDoNotThrow()
    {
        var cache = new ResilientDistributedCache(new DownCache(), NullLogger<ResilientDistributedCache>.Instance);

        Assert.Null(await cache.GetStringAsync("key"));
        await cache.SetStringAsync("key", "value");
        await cache.RemoveAsync("key");
        await cache.RefreshAsync("key");
    }

    [Fact]
    public async Task ResilientCache_StillPropagatesCancellation()
    {
        var cache = new ResilientDistributedCache(new CancelledCache(), NullLogger<ResilientDistributedCache>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.GetAsync("key"));
    }

    [Fact]
    public async Task Deduplicator_RecognizesOnlyTheSameOccurrence()
    {
        var deduplicator = new ReminderDeliveryDeduplicator(MemoryCache());
        var id = Guid.NewGuid();
        var today = new DateTime(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        Assert.False(await deduplicator.WasDeliveredAsync(id, today, default));

        await deduplicator.MarkDeliveredAsync(id, today, default);

        Assert.True(await deduplicator.WasDeliveredAsync(id, today, default));
        Assert.False(await deduplicator.WasDeliveredAsync(id, today.AddDays(1), default));
        Assert.False(await deduplicator.WasDeliveredAsync(Guid.NewGuid(), today, default));
    }

    [Theory]
    [InlineData("2030-06-01T10:00:00Z", "2030-06-01T10:00:30Z", "2030-06-02T10:00:00Z")]
    [InlineData("2030-06-01T10:00:00Z", "2030-06-02T10:00:00Z", "2030-06-03T10:00:00Z")]
    [InlineData("2030-06-01T10:00:00Z", "2030-06-06T09:00:00Z", "2030-06-06T10:00:00Z")]
    [InlineData("2030-06-01T10:00:00Z", "2030-06-06T11:00:00Z", "2030-06-07T10:00:00Z")]
    public void NextDailyOccurrence_SkipsMissedDays(string scheduled, string now, string expected)
    {
        var next = ReminderSchedule.NextDailyOccurrence(
            DateTime.Parse(scheduled).ToUniversalTime(),
            DateTime.Parse(now).ToUniversalTime());

        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), next);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(60, false)]
    [InlineData(61, true)]
    [InlineData(60 * 24 * 3, true)]
    public void IsStale_SkipsRemindersMoreThanAnHourLate(int minutesLate, bool expected)
    {
        var scheduled = new DateTime(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, ReminderSchedule.IsStale(scheduled, scheduled.AddMinutes(minutesLate)));
    }

    [Fact]
    public void ResetCode_IsSixAsciiDigits_AndHashIsBoundToItsRow()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => PasswordResetCode.Generate()).ToList();
        Assert.All(codes, c => Assert.True(PasswordResetCode.IsWellFormed(c), c));

        var id = Guid.NewGuid();
        var hash = PasswordResetCode.Hash(id, "123456");
        Assert.True(PasswordResetCode.Matches(id, "123456", hash));
        Assert.False(PasswordResetCode.Matches(id, "123457", hash));
        Assert.False(PasswordResetCode.Matches(Guid.NewGuid(), "123456", hash));
    }

    [Fact]
    public async Task ResendSender_PostsToEmailsEndpoint_WithBearerKeyAndBothBodies()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var sender = new ResendEmailSender(
            new HttpClient(handler) { BaseAddress = new Uri(ResendEmailSender.BaseAddress) },
            Options.Create(new EmailOptions { Provider = "Resend", ResendApiKey = "re_test", From = "ANITIME <no-reply@anitime.space>" }));

        await sender.SendAsync(new EmailMessage("user@example.com", "Subject", "Text 123456", "<p>Html</p>"), default);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.resend.com/emails", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer re_test", handler.Request.Headers.Authorization!.ToString());
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("ANITIME <no-reply@anitime.space>", body.GetProperty("from").GetString());
        Assert.Equal("user@example.com", body.GetProperty("to")[0].GetString());
        Assert.Equal("Subject", body.GetProperty("subject").GetString());
        Assert.Equal("Text 123456", body.GetProperty("text").GetString());
        Assert.Equal("<p>Html</p>", body.GetProperty("html").GetString());
    }

    [Fact]
    public async Task ResendSender_Failure_ThrowsWithoutTheMessageBody()
    {
        var sender = new ResendEmailSender(
            new HttpClient(new RecordingHandler(HttpStatusCode.UnprocessableEntity)) { BaseAddress = new Uri(ResendEmailSender.BaseAddress) },
            Options.Create(new EmailOptions { Provider = "Resend", ResendApiKey = "re_test", From = "a@b.c" }));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            sender.SendAsync(new EmailMessage("user@example.com", "S", "code 654321", "<p>654321</p>"), default));

        Assert.DoesNotContain("654321", error.Message);
    }

    [Theory]
    [InlineData(null, "re_key", "a@b.c", false)]
    [InlineData("Resend", "", "a@b.c", false)]
    [InlineData("Resend", "re_key", "", false)]
    [InlineData("resend", "re_key", "a@b.c", true)]
    public void EmailOptions_ResendNeedsProviderKeyAndSender(string? provider, string key, string from, bool expected) =>
        Assert.Equal(expected, new EmailOptions { Provider = provider, ResendApiKey = key, From = from }.IsResendConfigured);

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }
}
