using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Tests;

/// <summary>Password reset by emailed 6-digit code, and the legacy long-token path.</summary>
[Collection(ApiCollection.Name)]
public sealed partial class PasswordResetApiTests(ApiFixture fixture)
{
    private const string Password = "Test-Password-123";
    private const string NewPassword = "New-Password-456";
    private const string InvalidCodeMessage = "This code is wrong or has expired. Request a new one.";

    [GeneratedRegex(@"\b(\d{6})\b")]
    private static partial Regex CodePattern();

    private async Task<(HttpClient Client, string Email, string Refresh)> SignUpAsync()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        var client = fixture.AnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = Password, name = "Reset", gender = "Male" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (client, email, body.GetProperty("refreshToken").GetString()!);
    }

    private async Task<string> RequestCodeAsync(HttpClient client, string email, int emailNumber = 1)
    {
        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var message = await fixture.Emails.WaitForAsync(email, emailNumber);
        return CodePattern().Match(message.TextBody).Groups[1].Value;
    }

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string email, string code, string password = NewPassword) =>
        client.PostAsJsonAsync("/api/auth/reset-password", new { email, token = code, newPassword = password });

    private static async Task AssertInvalidCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(InvalidCodeMessage, body.GetProperty("message").GetString());
    }

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    private async Task<int> AttemptCountAsync(string email)
    {
        var count = 0;
        await fixture.WithDbAsync(async db => count = await db.PasswordResetTokens
            .Where(x => x.User.Email == email).OrderByDescending(x => x.CreatedAtUtc).Select(x => x.AttemptCount).FirstAsync());
        return count;
    }

    [Fact]
    public async Task EmailedCode_ResetsPassword_AndSignsOutOtherSessions()
    {
        var (client, email, refresh) = await SignUpAsync();

        var code = await RequestCodeAsync(client, email);
        var message = fixture.Emails.Sent.Last(x => x.To == email);
        Assert.Matches(@"^\d{6}$", code);
        Assert.StartsWith($"Your ANITIME code is {code}. It expires in 15 minutes.", message.TextBody);
        Assert.Contains(code, message.HtmlBody);

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, email, code)).StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var oldLogin = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var oldRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
    }

    [Fact]
    public async Task EmailCase_AndSurroundingSpaces_AreAccepted()
    {
        var (client, email, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);

        var response = await ResetAsync(client, $" {email.ToUpperInvariant()} ", $" {code} ");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UnknownEmail_GetsTheSameResponse_AndNoEmailIsSent()
    {
        var (client, email, _) = await SignUpAsync();
        var unknown = $"nobody-{Guid.NewGuid():N}@example.com";

        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var missing = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = unknown });

        Assert.Equal(known.StatusCode, missing.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        await fixture.Emails.WaitForAsync(email);
        Assert.DoesNotContain(fixture.Emails.Sent, x => x.To == unknown);
        await AssertInvalidCodeAsync(await ResetAsync(client, unknown, "123456"));
    }

    [Fact]
    public async Task Response_NeverContainsTheCode_OutsideDevelopment()
    {
        var (client, email, _) = await SignUpAsync();

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = CodePattern().Match((await fixture.Emails.WaitForAsync(email)).TextBody).Groups[1].Value;

        Assert.Equal(JsonValueKind.Null, body.GetProperty("debugToken").ValueKind);
        Assert.DoesNotContain(code, body.GetRawText());
    }

    [Fact]
    public async Task Code_IsNeverWrittenToLogs()
    {
        var (client, email, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);
        await AssertInvalidCodeAsync(await ResetAsync(client, email, WrongCode(code)));
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, email, code)).StatusCode);

        Assert.DoesNotContain(fixture.Logs, line => line.Contains(code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WrongCode_ReturnsClearMessage_AndTheRightCodeStillWorks()
    {
        var (client, email, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);

        await AssertInvalidCodeAsync(await ResetAsync(client, email, WrongCode(code)));

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, email, code)).StatusCode);
    }

    [Fact]
    public async Task FiveWrongCodes_BurnTheCode()
    {
        var (client, email, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);

        for (var i = 0; i < 5; i++)
        {
            await AssertInvalidCodeAsync(await ResetAsync(client, email, WrongCode(code)));
        }

        await AssertInvalidCodeAsync(await ResetAsync(client, email, code));
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task FourWrongCodes_ThenTheRightOne_Works()
    {
        var (client, email, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);

        for (var i = 0; i < 4; i++)
        {
            await AssertInvalidCodeAsync(await ResetAsync(client, email, WrongCode(code)));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, email, code)).StatusCode);
    }

    [Fact]
    public async Task ParallelGuesses_CannotExceedTheAttemptLimit()
    {
        var (client, email, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);

        var guesses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ResetAsync(client, email, WrongCode(code))));

        Assert.All(guesses, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(5, await AttemptCountAsync(email));
        await AssertInvalidCodeAsync(await ResetAsync(client, email, code));
    }

    [Fact]
    public async Task MalformedCode_IsRejected_WithoutUsingAnAttempt()
    {
        var (client, email, _) = await SignUpAsync();
        await RequestCodeAsync(client, email);

        foreach (var malformed in new[] { "12345", "1234567", "abcdef", "12 456", "١٢٣٤٥٦" })
        {
            await AssertInvalidCodeAsync(await ResetAsync(client, email, malformed));
        }

        Assert.Equal(0, await AttemptCountAsync(email));
    }

    [Fact]
    public async Task NewCode_ReplacesTheEarlierOne()
    {
        var (client, email, _) = await SignUpAsync();
        var first = await RequestCodeAsync(client, email);
        var second = await RequestCodeAsync(client, email, emailNumber: 2);

        if (first != second)
        {
            await AssertInvalidCodeAsync(await ResetAsync(client, email, first));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, email, second)).StatusCode);
    }

    [Fact]
    public async Task ExpiredCode_IsRejected()
    {
        var (client, email, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);
        await fixture.WithDbAsync(db => db.PasswordResetTokens.Where(x => x.User.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1))));

        await AssertInvalidCodeAsync(await ResetAsync(client, email, code));
    }

    [Fact]
    public async Task Code_WorksOnlyOnce_EvenWhenSentTwiceAtTheSameTime()
    {
        var (client, email, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);

        var responses = await Task.WhenAll(
            ResetAsync(client, email, code, "Parallel-Password-1"),
            ResetAsync(client, email, code, "Parallel-Password-2"));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        await AssertInvalidCodeAsync(await ResetAsync(client, email, code));
    }

    [Fact]
    public async Task Code_OnlyWorksForTheEmailItWasSentTo()
    {
        var (client, email, _) = await SignUpAsync();
        var (_, otherEmail, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);
        await RequestCodeAsync(client, otherEmail);

        var response = await ResetAsync(client, otherEmail, code);

        // The other user's own code could equal this one by chance (1 in a million); then the reset rightly succeeds.
        var otherCode = CodePattern().Match(fixture.Emails.Sent.Last(x => x.To == otherEmail).TextBody).Groups[1].Value;
        if (otherCode != code)
        {
            await AssertInvalidCodeAsync(response);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, email, code)).StatusCode);
    }

    [Fact]
    public async Task CodeWithoutEmail_DoesNotWork_OnTheLegacyPath()
    {
        var (client, email, _) = await SignUpAsync();
        var code = await RequestCodeAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new { token = code, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, email, code)).StatusCode);
    }

    [Fact]
    public async Task InvalidEmailFormat_Returns400Validation()
    {
        var client = fixture.AnonymousClient();

        var response = await ResetAsync(client, "not-an-email", "123456");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }
}
