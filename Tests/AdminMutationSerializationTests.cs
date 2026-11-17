using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MyLife.Shared.Data;
using Npgsql;
using Xunit;

public sealed class AdminMutationSerializationTests(MyLifeFactory factory) : IClassFixture<MyLifeFactory>
{
    [Theory]
    [InlineData("role")]
    [InlineData("status")]
    [InlineData("delete")]
    public async Task Queued_admin_mutation_revalidates_revoked_caller_after_transaction_lock(string action)
    {
        var observer = new LockObserver();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(observer))));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Client-Platform", "mobile");
        var actorEmail = $"m9-lock-actor.{Guid.NewGuid():N}@gmail.com";
        var targetEmail = $"m9-lock-target.{Guid.NewGuid():N}@gmail.com";
        var password = "M9_Test!" + Guid.NewGuid().ToString("N");
        foreach (var email in new[] { actorEmail, targetEmail })
        {
            using var registration = await client.PostAsJsonAsync("/api/auth/register", new
            {
                fullName = "Synthetic lock regression", phoneNumber = "0912345678", gender = "Nam",
                dateOfBirth = "1995-05-20", email, password, confirmPassword = password
            });
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        }
        await using var connection = new NpgsqlConnection(MyLifeFactory.ConnectionString);
        await connection.OpenAsync();
        await using (var grant = new NpgsqlCommand("UPDATE user_roles SET role_id=(SELECT id FROM roles WHERE name='ADMIN') WHERE user_id=(SELECT id FROM users WHERE email=@email)", connection))
        {
            grant.Parameters.AddWithValue("email", actorEmail); Assert.Equal(1, await grant.ExecuteNonQueryAsync());
        }
        int targetId;
        await using (var target = new NpgsqlCommand("SELECT id FROM users WHERE email=@email", connection))
        {
            target.Parameters.AddWithValue("email", targetEmail); targetId = Convert.ToInt32(await target.ExecuteScalarAsync());
        }
        using var login = await client.PostAsJsonAsync("/api/login", new { email = actorEmail, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        await using var heldLock = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(741003002)", connection, heldLock)) await command.ExecuteNonQueryAsync();
        var route = $"/api/admin/users/{targetId}" + (action == "delete" ? "" : $"/{action}");
        using var request = new HttpRequestMessage(action == "delete" ? HttpMethod.Delete : HttpMethod.Put, route);
        request.Headers.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        if (action == "role") request.Content = JsonContent.Create(new { role = "ADMIN" });
        if (action == "status") request.Content = JsonContent.Create(new { isActive = false });
        var pending = client.SendAsync(request);
        // The command interceptor signals only after authentication and entry into
        // the controller transaction. No scheduling delay is used to guess this.
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using (var revokeConnection = new NpgsqlConnection(MyLifeFactory.ConnectionString))
        {
            await revokeConnection.OpenAsync();
            await using var revoke = new NpgsqlCommand("UPDATE refresh_tokens SET is_revoked=true WHERE user_id=(SELECT id FROM users WHERE email=@email)", revokeConnection);
            revoke.Parameters.AddWithValue("email", actorEmail); Assert.True(await revoke.ExecuteNonQueryAsync() > 0);
        }
        await heldLock.CommitAsync();
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var state = new NpgsqlCommand("SELECT u.is_active AND r.name='USER' FROM users u JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id WHERE u.id=@id", connection);
        state.Parameters.AddWithValue("id", targetId); Assert.Equal(true, await state.ExecuteScalarAsync());
    }

    private sealed class LockObserver : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock(741003002)", StringComparison.Ordinal)) Started.TrySetResult();
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
