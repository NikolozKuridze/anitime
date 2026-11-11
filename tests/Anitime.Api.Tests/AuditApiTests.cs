using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Tests;

/// <summary>Edge cases found by the full audit, outside the notes save flow.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuditApiTests(ApiFixture fixture)
{
    private const string Password = "Test-Password-123";
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];

    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();

    private static HashSet<string> Keys(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).ToHashSet();

    private static void AssertNot500(HttpResponseMessage response) =>
        Assert.True((int)response.StatusCode < 500, $"got {(int)response.StatusCode}");

    private async Task<(HttpClient Client, string Email, string Refresh)> SignUpAsync()
    {
        var email = $"audit-{Guid.NewGuid():N}@example.com";
        var client = fixture.AnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = Password, name = "Audit", gender = "Male" });
        response.EnsureSuccessStatusCode();
        var body = await Json(response);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return (client, email, body.GetProperty("refreshToken").GetString()!);
    }

    private static MultipartFormDataContent Image(byte[] bytes, string contentType, string fileName = "photo.jpg")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    // ---- cache shape ----

    [Fact]
    public async Task Dashboard_CacheHit_HasSameJsonShapeAsMiss()
    {
        var client = await fixture.NewUserClientAsync();

        var miss = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        var hit = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");

        Assert.Equal(Keys(miss), Keys(hit));
    }

    [Fact]
    public async Task ExerciseFilters_CacheHit_HasSameJsonShapeAsMiss()
    {
        var client = await fixture.NewUserClientAsync();

        var first = await client.GetFromJsonAsync<JsonElement>("/api/exercises/filters");
        var second = await client.GetFromJsonAsync<JsonElement>("/api/exercises/filters");

        Assert.Equal(Keys(first), Keys(second));
        Assert.True(second.TryGetProperty("muscles", out _));
    }

    // ---- query and header bounds ----

    [Theory]
    [InlineData("year=0&month=1")]
    [InlineData("year=9999&month=12")]
    [InlineData("year=-5&month=1")]
    public async Task Calendar_OutOfRangeYear_Returns400(string query)
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.GetAsync($"/api/calendar?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("to=0001-01-01")]
    [InlineData("from=9999-12-01&to=9999-12-31")]
    public async Task Analytics_ExtremeDates_DoNotCrash(string query)
    {
        var client = await fixture.NewUserClientAsync();

        AssertNot500(await client.GetAsync($"/api/analytics?{query}"));
    }

    [Fact]
    public async Task Analytics_RangeOverLimit_Returns400()
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.GetAsync("/api/analytics?from=0001-01-01&to=9999-12-31");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Exercises_HugePageNumber_ReturnsEmptyPage()
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.GetAsync($"/api/exercises?page={int.MaxValue}&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/exercises?search=a%00b")]
    [InlineData("/api/exercises?muscle=%00")]
    public async Task NulInUrl_Returns400(string url)
    {
        var client = await fixture.NewUserClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("-2000000000")]
    [InlineData("2000000000")]
    public async Task ExtremeTimezoneOffsetHeader_DoesNotCrash(string offset)
    {
        var client = await fixture.NewUserClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        request.Headers.Add("X-Timezone-Offset-Minutes", offset);

        AssertNot500(await client.SendAsync(request));
    }

    // ---- NUL in any JSON string ----

    [Fact]
    public async Task NulInTaskTitle_IsStripped_Not500()
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/tasks", new { title = "bu\u0000y", date = "2026-11-07" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("buy", (await Json(response)).GetProperty("title").GetString());
    }

    // ---- reminders ----

    [Theory]
    [InlineData("2030-06-01T10:00:00", "2030-06-01T10:00:00")]
    [InlineData("2030-06-01T10:00:00Z", "2030-06-01T10:00:00")]
    [InlineData("2030-06-01T14:00:00+04:00", "2030-06-01T10:00:00")]
    public async Task Reminder_AnyDateFormat_IsStoredAsUtc(string scheduled, string expectedUtc)
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/reminders", new
        {
            title = "Call",
            message = "Call back",
            scheduledForUtc = scheduled,
            repeatDaily = false
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = (await Json(response)).GetProperty("scheduledForUtc").GetDateTime().ToUniversalTime();
        Assert.Equal(DateTime.Parse(expectedUtc + "Z").ToUniversalTime(), stored);
    }

    // ---- notes and tasks ----

    [Fact]
    public async Task DuplicatingNoteWithMaxLengthTitle_Works()
    {
        var client = await fixture.NewUserClientAsync();
        var created = await client.PostAsJsonAsync("/api/notes", new
        {
            title = new string('t', 160),
            contentJson = new { version = 1, blocks = Array.Empty<object>() },
            fontSize = 16,
            fontWeight = 400,
            isPinned = false,
            isArchived = false
        });
        created.EnsureSuccessStatusCode();
        var id = (await Json(created)).GetProperty("id").GetString();

        var response = await client.PostAsync($"/api/notes/{id}/duplicate", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("title").GetString()!.Length <= 160);
    }

    [Fact]
    public async Task ClientRequestId_ReusedAfterDelete_CreatesNewTask()
    {
        var client = await fixture.NewUserClientAsync();
        var body = new { title = "Task", date = "2026-11-07", clientRequestId = Guid.NewGuid().ToString("N") };
        var first = await client.PostAsJsonAsync("/api/tasks", body);
        var id = (await Json(first)).GetProperty("id").GetString();
        (await client.DeleteAsync($"/api/tasks/{id}")).EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/tasks", body);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(id, (await Json(second)).GetProperty("id").GetString());
    }

    [Fact]
    public async Task ChecklistItemOfDeletedTask_Returns404()
    {
        var client = await fixture.NewUserClientAsync();
        var task = await Json(await client.PostAsJsonAsync("/api/tasks", new
        {
            title = "Task",
            date = "2026-11-07",
            checklistItems = new[] { new { title = "Item" } }
        }));
        var itemId = task.GetProperty("checklistItems")[0].GetProperty("id").GetString();
        (await client.DeleteAsync($"/api/tasks/{task.GetProperty("id").GetString()}")).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/tasks/checklist/{itemId}/toggle", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- refresh tokens ----

    [Fact]
    public async Task ParallelRefreshWithSameToken_AllSucceed()
    {
        var (client, _, refresh) = await SignUpAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task RefreshRetriedWithinGraceWindow_Succeeds()
    {
        var (client, _, refresh) = await SignUpAsync();

        var first = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        var retry = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task RefreshReplayedAfterGraceWindow_Returns401()
    {
        var (client, _, refresh) = await SignUpAsync();
        (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).EnsureSuccessStatusCode();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refresh)));
        var past = DateTime.UtcNow.AddMinutes(-5);
        await fixture.WithDbAsync(db => db.RefreshTokens.Where(x => x.TokenHash == hash)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReplacedAtUtc, past).SetProperty(x => x.RevokedAtUtc, past)));

        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task RefreshAfterLogout_Returns401()
    {
        var (client, _, refresh) = await SignUpAsync();
        (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = refresh })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PasswordReset_ClosesRefreshGraceWindow()
    {
        var (client, email, refresh) = await SignUpAsync();
        (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).EnsureSuccessStatusCode();

        const string rawReset = "reset-token-for-test";
        var resetHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawReset + email)));
        await fixture.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(x => x.Email == email);
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = resetHash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        });

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new { token = rawReset + email, newPassword = "New-Password-456" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    // ---- passwords ----

    [Fact]
    public async Task Login_UpgradesOldPasswordHashes_AndStillAuthenticates()
    {
        var (client, email, _) = await SignUpAsync();
        var salt = RandomNumberGenerator.GetBytes(16);
        var oldHash = Rfc2898DeriveBytes.Pbkdf2(Password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var stored = $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(oldHash)}";
        await fixture.WithDbAsync(db => db.Users.Where(x => x.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PasswordHash, stored)));

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        string? current = null;
        await fixture.WithDbAsync(async db => current = await db.Users.Where(x => x.Email == email).Select(x => x.PasswordHash).FirstAsync());
        Assert.StartsWith("600000.", current);
    }

    [Fact]
    public async Task ForgotPassword_DoesNotWriteResetTokenToLogs()
    {
        var (client, email, _) = await SignUpAsync();
        fixture.Logs.Clear();

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string? tokenHash = null;
        await fixture.WithDbAsync(async db => tokenHash = await db.PasswordResetTokens
            .Where(x => x.User.Email == email).Select(x => x.TokenHash).FirstAsync());
        Assert.NotNull(tokenHash);
        Assert.DoesNotContain(fixture.Logs, m => m.Contains("Password reset token", StringComparison.OrdinalIgnoreCase)
            && System.Text.RegularExpressions.Regex.IsMatch(m, "[0-9A-F]{64}"));
    }

    // ---- rate limiting ----

    [Fact]
    public async Task LoginOverLimit_Returns429WithJson_PerClientIp()
    {
        var client = fixture.ClientWithAuthRateLimit(permitLimit: 3);
        var body = new { email = "nobody@example.com", password = "Wrong-Password-1" };
        var ip = $"203.0.113.{Random.Shared.Next(1, 250)}";

        async Task<HttpResponseMessage> Attempt(string fromIp)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(body) };
            request.Headers.Add("CF-Connecting-IP", fromIp);
            return await client.SendAsync(request);
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Attempt(ip)).StatusCode);
        }

        var limited = await Attempt(ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", (await Json(limited)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Attempt("198.51.100.7")).StatusCode);
    }

    // ---- image uploads ----

    [Fact]
    public async Task Upload_ValidImage_IsServedWithDetectedContentType()
    {
        var client = await fixture.NewUserClientAsync();

        var upload = await client.PostAsync("/api/profile/profile-image", Image(Jpeg, "image/png", "photo.html"));
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);

        var image = await client.GetAsync("/api/profile/profile-image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Upload_NonImageClaimingToBeImage_Returns400()
    {
        var client = await fixture.NewUserClientAsync();
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");

        var response = await client.PostAsync("/api/profile/profile-image", Image(html, "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(5 * 1024 * 1024 + 1)]
    [InlineData(8 * 1024 * 1024)]
    public async Task Upload_OverFiveMegabytes_Returns413WithJsonMessage(int size)
    {
        var client = await fixture.NewUserClientAsync();
        var big = new byte[size];
        Jpeg.CopyTo(big, 0);

        var response = await client.PostAsync("/api/profile/background-image", Image(big, "image/jpeg"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("payload_too_large", body.GetProperty("code").GetString());
        Assert.Equal("Image is too large. Maximum size is 5 MB.", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Upload_ExactlyFiveMegabytes_IsAccepted()
    {
        var client = await fixture.NewUserClientAsync();
        var image = new byte[5 * 1024 * 1024];
        Jpeg.CopyTo(image, 0);

        var response = await client.PostAsync("/api/profile/profile-image", Image(image, "image/jpeg"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Upload_OverTheRequestCap_Returns413BeforeReadingTheBody()
    {
        var client = await fixture.NewUserClientAsync();
        var huge = new byte[26 * 1024 * 1024];
        Jpeg.CopyTo(huge, 0);

        var response = await client.PostAsync("/api/profile/profile-image", Image(huge, "image/jpeg"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("Image is too large. Maximum size is 5 MB.", (await Json(response)).GetProperty("message").GetString());
    }

    // ---- proxy ----

    [Fact]
    public async Task ImageUrls_UseHttps_WhenProxyReportsIt()
    {
        var client = await fixture.NewUserClientAsync();
        (await client.PostAsync("/api/profile/profile-image", Image(Jpeg, "image/jpeg"))).EnsureSuccessStatusCode();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var profile = await Json(await client.SendAsync(request));

        Assert.StartsWith("https://", profile.GetProperty("profileImageUrl").GetString());
    }
}
