using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyLife.Shared.Data;
using Xunit;

public sealed class FamilyIsolationTests(MyLifeFactory factory) : IClassFixture<MyLifeFactory>
{
    private readonly HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    [Fact]
    public async Task Users_have_private_trees_same_names_and_cannot_attack_foreign_ids()
    {
        var a = await User(); var b = await User();
        var aId = await Create(a.Token, "Nguyễn Văn An", new() { ["familyTreeId"] = 999999L, ["ownerUserId"] = b.Id, ["userId"] = b.Id });
        var aChild = await Create(a.Token, "A Child", new() { ["fatherId"] = aId });
        var bId = await Create(b.Token, "Nguyễn Văn An"); var bChild = await Create(b.Token, "B Child", new() { ["fatherId"] = bId });
        Assert.Equal(new[] { aId, aChild }, (await List(a.Token)).Select(m => m.GetProperty("id").GetInt32()).Order().ToArray());
        Assert.Equal(new[] { bId, bChild }, (await List(b.Token)).Select(m => m.GetProperty("id").GetInt32()).Order().ToArray());
        using var before = await Send(a.Token, HttpMethod.Get, $"/api/family-tree/{aId}");
        var original = await before.Content.ReadAsStringAsync();
        foreach (var (attacker, id) in new[] { (b.Token, aId), (a.Token, bId) })
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
            {
                using var denied = await Send(attacker, method, $"/api/family-tree/{id}", Member("Attack"));
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                Assert.Equal("FAMILY_MEMBER_NOT_FOUND", (await Body(denied)).GetProperty("code").GetString());
            }
        using var after = await Send(a.Token, HttpMethod.Get, $"/api/family-tree/{aId}");
        Assert.Equal(original, await after.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.FamilyTrees.CountAsync(t => t.OwnerUserId == a.Id));
        Assert.Equal(1, await db.FamilyTrees.CountAsync(t => t.OwnerUserId == b.Id));
        Assert.Equal(a.Id, await db.FamilyMembers.Where(m => m.Id == aId).Select(m => m.FamilyTree.OwnerUserId).SingleAsync());
    }
    [Fact]
    public async Task Foreign_parent_spouse_child_and_horizontal_references_are_rejected_on_create_and_update()
    {
        var a = await User(); var b = await User(); var foreign = await Create(a.Token, "A Member"); var own = await Create(b.Token, "B Member");
        var references = new Dictionary<string, object?>[] {
            new() { ["fatherId"] = foreign }, new() { ["motherId"] = foreign }, new() { ["spouseId"] = foreign },
            new() { ["childIds"] = new[] { foreign } }, new() { ["horizontalRelations"] = new[] { new { memberId = foreign, relationType = "Sibling" } } }
        };
        foreach (var reference in references)
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
            {
                using var response = await Send(b.Token, method, method == HttpMethod.Post ? "/api/family-tree" : $"/api/family-tree/{own}", Member("Attempt", reference));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("FAMILY_RELATED_MEMBER_NOT_FOUND", (await Body(response)).GetProperty("code").GetString());
            }
        Assert.Single(await List(a.Token)); Assert.Single(await List(b.Token));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.FamilyRelationships.AnyAsync(r => r.Member1!.FamilyTreeId != r.Member2!.FamilyTreeId));
        Assert.Null((await db.FamilyMembers.FindAsync(foreign))!.SpouseId);
        Assert.Null((await db.FamilyMembers.FindAsync(own))!.FatherId);
    }
    [Fact]
    public async Task Concurrent_first_requests_create_exactly_one_tree()
    {
        var user = await User();
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Send(user.Token, HttpMethod.Get, "/api/family-tree")));
        foreach (var response in responses) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Empty((await Body(response)).GetProperty("data").EnumerateArray()); response.Dispose(); }
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().FamilyTrees.CountAsync(t => t.OwnerUserId == user.Id));
    }
    [Fact]
    public async Task Same_tree_relationships_and_delete_cleanup_preserve_other_tree()
    {
        var a = await User(); var b = await User();
        var parent = await Create(a.Token, "Parent"); var spouse = await Create(a.Token, "Spouse"); var child = await Create(a.Token, "Child", new() { ["fatherId"] = parent });
        var bMember = await Create(b.Token, "Other tree");
        using var linked = await Send(a.Token, HttpMethod.Put, $"/api/family-tree/{parent}", Member("Parent", new() {
            ["spouseId"] = spouse, ["childIds"] = new[] { child }, ["horizontalRelations"] = new[] { new { memberId = spouse, relationType = "Sibling" } } }));
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        using var removed = await Send(a.Token, HttpMethod.Delete, $"/api/family-tree/{parent}"); Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var remaining = await List(a.Token);
        Assert.Equal(JsonValueKind.Null, remaining.Single(m => m.GetProperty("id").GetInt32() == child).GetProperty("fatherId").ValueKind);
        Assert.Equal(JsonValueKind.Null, remaining.Single(m => m.GetProperty("id").GetInt32() == spouse).GetProperty("spouseId").ValueKind);
        Assert.Equal(bMember, Assert.Single(await List(b.Token)).GetProperty("id").GetInt32());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().FamilyRelationships.AnyAsync(r => r.Member1Id == parent || r.Member2Id == parent));
    }
    private static Dictionary<string, object?> Member(string name, Dictionary<string, object?>? fields = null)
    {
        var dto = new Dictionary<string, object?> { ["fullName"] = name, ["generation"] = 1, ["gender"] = "Nam" };
        if (fields is not null) foreach (var pair in fields) dto[pair.Key] = pair.Value;
        return dto;
    }
    private async Task<(string Token, int Id)> User()
    {
        var email = $"tree.{Guid.NewGuid():N}@gmail.com"; const string password = "User_Test!234567";
        using var registration = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Tree User", phoneNumber = "0912345678", gender = "Nam", dateOfBirth = "1995-05-20", email, password, confirmPassword = password });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/login") { Content = JsonContent.Create(new { email, password }) }; request.Headers.Add("X-Client-Platform", "mobile");
        using var login = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, login.StatusCode); var body = await Body(login);
        Assert.Equal("USER", body.GetProperty("user").GetProperty("role").GetString());
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("user").GetProperty("id").GetInt32());
    }
    private async Task<int> Create(string token, string name, Dictionary<string, object?>? fields = null)
    {
        using var response = await Send(token, HttpMethod.Post, "/api/family-tree", Member(name, fields)); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("data").GetProperty("id").GetInt32();
    }
    private async Task<JsonElement[]> List(string token) { using var response = await Send(token, HttpMethod.Get, "/api/family-tree"); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("data").EnumerateArray().ToArray(); }
    private Task<HttpResponseMessage> Send(string token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) }; request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
}
