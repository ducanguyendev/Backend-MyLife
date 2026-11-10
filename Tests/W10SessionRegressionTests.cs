using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public sealed class W10SessionRegressionTests(MyLifeFactory factory) : IClassFixture<MyLifeFactory>
{
    [Fact]
    public async Task Logout_revokes_only_its_access_session_and_rotation_rejects_the_previous_access_token()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Client-Platform", "mobile");
        var email = $"w10-session.{Guid.NewGuid():N}@gmail.com";
        const string password = "Session_Test!234567";
        using var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Session Test", phoneNumber = "0912345678", gender = "Nam", dateOfBirth = "1995-05-20",
            email, password, confirmPassword = password
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        async Task<JsonElement> Login()
        {
            using var response = await client.PostAsJsonAsync("/api/login", new { email, password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
        }
        async Task<HttpStatusCode> Me(JsonElement session)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            request.Headers.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
        var first = await Login();
        var second = await Login();
        Assert.Equal(HttpStatusCode.OK, await Me(first));
        using var logout = await client.PostAsJsonAsync("/api/logout", first);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await Me(first));
        Assert.Equal(HttpStatusCode.OK, await Me(second));
        using var rotation = await client.PostAsJsonAsync("/api/refresh-token", second);
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);
        var rotated = await rotation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Unauthorized, await Me(second));
        Assert.Equal(HttpStatusCode.OK, await Me(rotated));
        using var replay = await client.PostAsJsonAsync("/api/refresh-token", second);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await Me(rotated));
    }
}
