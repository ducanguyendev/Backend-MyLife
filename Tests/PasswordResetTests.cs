using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyLife.Features.Auth.Services;
using MyLife.Shared.Data;
using Xunit;

namespace MyLife.Tests;

public sealed class TestEmailSender : IEmailSender
{
    private readonly List<(string To, string Subject, string HtmlBody, string TextBody)> _sent = new();

    public IReadOnlyList<(string To, string Subject, string HtmlBody, string TextBody)> Sent
    {
        get { lock (_sent) return _sent.ToList(); }
    }

    public Task SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default)
    {
        lock (_sent) _sent.Add((to, subject, htmlBody, textBody));
        return Task.CompletedTask;
    }
}

public sealed class PasswordResetTestFactory : MyLifeFactory
{
    public TestEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }
}

public sealed class PasswordResetTests(PasswordResetTestFactory factory) : IClassFixture<PasswordResetTestFactory>
{
    private readonly HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private const string Password = "User_Test!234567";
    private const string NewPassword = "Reset_Test!987654";

    [Fact]
    public async Task ForgotPassword_unknown_email_returns_success_without_sending_email()
    {
        var email = $"reset-unknown.{Guid.NewGuid():N}@gmail.com";
        using var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(factory.Emails.Sent, sent => sent.To == email);
    }

    [Fact]
    public async Task ForgotPassword_known_email_sends_reset_link()
    {
        var email = await RegisterUserAsync();
        using var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(factory.Emails.Sent, sent => sent.To == email);
        Assert.Contains("/reset-password?token=", sent.TextBody);
        Assert.Contains("/reset-password?token=", sent.HtmlBody);
    }

    [Fact]
    public async Task ResetPassword_with_valid_token_changes_password()
    {
        var email = await RegisterUserAsync();
        var token = await RequestResetTokenAsync(email);

        using var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token, newPassword = NewPassword, confirmPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        using var oldLogin = await client.PostAsJsonAsync("/api/login", new { email, password = Password, rememberMe = false });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        using var newLogin = await client.PostAsJsonAsync("/api/login", new { email, password = NewPassword, rememberMe = false });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_token_cannot_be_reused()
    {
        var email = await RegisterUserAsync();
        var token = await RequestResetTokenAsync(email);

        using var first = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token, newPassword = NewPassword, confirmPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token, newPassword = "Another_Test!123456", confirmPassword = "Another_Test!123456" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("AUTH_RESET_TOKEN_INVALID", body);
    }

    [Fact]
    public async Task ResetPassword_expired_token_is_rejected()
    {
        var email = await RegisterUserAsync();
        var token = await RequestResetTokenAsync(email);
        await ExpireResetTokenAsync(email);

        using var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token, newPassword = NewPassword, confirmPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        var body = await reset.Content.ReadAsStringAsync();
        Assert.Contains("AUTH_RESET_TOKEN_INVALID", body);
    }

    [Fact]
    public async Task ResetPassword_wrong_token_is_rejected()
    {
        await RegisterUserAsync();
        using var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token = "this-token-does-not-exist", newPassword = NewPassword, confirmPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        var body = await reset.Content.ReadAsStringAsync();
        Assert.Contains("AUTH_RESET_TOKEN_INVALID", body);
    }

    [Fact]
    public async Task ResetPassword_weak_password_is_rejected()
    {
        var email = await RegisterUserAsync();
        var token = await RequestResetTokenAsync(email);
        using var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token, newPassword = "weak", confirmPassword = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    private async Task<string> RegisterUserAsync()
    {
        var email = $"reset.{Guid.NewGuid():N}@gmail.com";
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Reset Test",
            phoneNumber = "0912345678",
            gender = "Nam",
            dateOfBirth = "1995-05-20",
            email,
            password = Password,
            confirmPassword = Password,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private async Task<string> RequestResetTokenAsync(string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(factory.Emails.Sent, sent => sent.To == email);
        var match = Regex.Match(sent.TextBody, @"token=([A-Za-z0-9_-]+)");
        Assert.True(match.Success, "Reset email did not contain a token link.");
        return match.Groups[1].Value;
    }

    private static async Task ExpireResetTokenAsync(string email)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(MyLifeFactory.ConnectionString)
            .Options;
        await using var db = new AppDbContext(options);
        var user = await db.Users.SingleAsync(x => x.Email == email);
        var record = await db.PasswordResetTokens.SingleAsync(x => x.UserId == user.Id && x.UsedAt == null);
        record.ExpiresAt = DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();
    }
}

// Uses the real SmtpEmailSender with no Smtp configuration: requesting a reset
// for a real account must fail loudly instead of pretending the email was sent.
public sealed class PasswordResetSmtpTests(MyLifeFactory factory) : IClassFixture<MyLifeFactory>
{
    private readonly HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [Fact]
    public async Task ForgotPassword_without_smtp_config_returns_500()
    {
        var email = $"reset-smtp.{Guid.NewGuid():N}@gmail.com";
        using var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Smtp Test",
            phoneNumber = "0912345678",
            gender = "Nam",
            dateOfBirth = "1995-05-20",
            email,
            password = "User_Test!234567",
            confirmPassword = "User_Test!234567",
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("AUTH_SMTP_NOT_CONFIGURED", body);
    }
}
