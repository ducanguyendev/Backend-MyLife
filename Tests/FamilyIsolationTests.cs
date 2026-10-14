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
    public async Task Family_map_is_role_protected_tree_scoped_and_uses_persistent_location_cache()
    {
        using (var anonymous = await client.GetAsync("/api/family-tree/map")) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var admin = await AdminToken();
        using (var adminResponse = await Send(admin, HttpMethod.Get, "/api/family-tree/map")) Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);

        var a = await User(); var b = await User();
        var aMember = await Create(a.Token, "A member", new() { ["address"] = "Quận 1, TP. Hồ Chí Minh" });
        var bMember = await Create(b.Token, "B private member", new() { ["address"] = "Hà Nội" });
        var geocoder = factory.Services.GetRequiredService<TestGeocodingService>();
        var initialGeocoderCalls = geocoder.Calls;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cacheA = await db.FamilyMembers.SingleAsync(x => x.Id == aMember);
            cacheA.Latitude = 10.77; cacheA.Longitude = 106.69; cacheA.GeocodedAddress = cacheA.Address;
            cacheA.LocationDisplayName = "Ho Chi Minh City, Vietnam"; cacheA.GeocodedAt = DateTime.UtcNow;
            var cacheB = await db.FamilyMembers.SingleAsync(x => x.Id == bMember);
            cacheB.Latitude = 21.02; cacheB.Longitude = 105.84; cacheB.GeocodedAddress = cacheB.Address;
            cacheB.LocationDisplayName = "Hanoi, Vietnam"; cacheB.GeocodedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        using (var mapA = await Send(a.Token, HttpMethod.Get, "/api/family-tree/map"))
        {
            Assert.Equal(HttpStatusCode.OK, mapA.StatusCode);
            var result = (await Body(mapA)).GetProperty("data");
            Assert.Equal(1, result.GetProperty("totalMembers").GetInt32());
            Assert.Equal("Ho Chi Minh City, Vietnam", result.GetProperty("locations")[0].GetProperty("displayName").GetString());
            Assert.Equal(10.8, result.GetProperty("locations")[0].GetProperty("latitude").GetDouble());
            Assert.Equal(106.7, result.GetProperty("locations")[0].GetProperty("longitude").GetDouble());
            Assert.Equal(aMember, result.GetProperty("locations")[0].GetProperty("members")[0].GetProperty("id").GetInt32());
        }
        using (var mapB = await Send(b.Token, HttpMethod.Get, "/api/family-tree/map"))
        {
            var result = (await Body(mapB)).GetProperty("data");
            Assert.Equal(bMember, result.GetProperty("locations")[0].GetProperty("members")[0].GetProperty("id").GetInt32());
        }
        Assert.Equal(initialGeocoderCalls, geocoder.Calls); // unchanged cached addresses must not trigger another lookup

        using (var updated = await Send(a.Token, HttpMethod.Put, $"/api/family-tree/{aMember}", Member("A member", new() { ["address"] = "Đà Nẵng" })))
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var changed = await scope.ServiceProvider.GetRequiredService<AppDbContext>().FamilyMembers.SingleAsync(x => x.Id == aMember);
            Assert.Null(changed.Latitude); Assert.Null(changed.Longitude); Assert.Null(changed.GeocodedAddress);
            Assert.Null(changed.LocationDisplayName); Assert.Null(changed.GeocodedAt);
        }

        var withoutAddress = await User();
        await Create(withoutAddress.Token, "No address");
        using (var noAddressMap = await Send(withoutAddress.Token, HttpMethod.Get, "/api/family-tree/map"))
        {
            Assert.Equal(HttpStatusCode.OK, noAddressMap.StatusCode);
            Assert.Equal(0, (await Body(noAddressMap)).GetProperty("data").GetProperty("pendingGeocodingMembers").GetInt32());
        }
        Assert.Equal(initialGeocoderCalls, geocoder.Calls);
    }

    [Fact]
    public async Task Family_map_returns_without_waiting_for_geocoding_and_caches_success_and_failure()
    {
        var user = await User();
        var geocoder = factory.Services.GetRequiredService<TestGeocodingService>();
        var initialGeocoderCalls = geocoder.Calls;
        var locatedId = await Create(user.Token, "Locatable", new() { ["address"] = "Quận 1, TP. Hồ Chí Minh" });
        using (var first = await Send(user.Token, HttpMethod.Get, "/api/family-tree/map"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(1, (await Body(first)).GetProperty("data").GetProperty("pendingGeocodingMembers").GetInt32());
        }
        var found = await WaitForGeocodeAsync(locatedId);
        Assert.NotNull(found.Latitude); Assert.Equal("Ho Chi Minh City, Vietnam", found.LocationDisplayName);
        using (var cached = await Send(user.Token, HttpMethod.Get, "/api/family-tree/map"))
        {
            var result = (await Body(cached)).GetProperty("data");
            Assert.Equal(1, result.GetProperty("locatedMembers").GetInt32());
            Assert.Equal(0, result.GetProperty("pendingGeocodingMembers").GetInt32());
        }

        var unresolvedId = await Create(user.Token, "Unresolved", new() { ["address"] = "unresolved address" });
        using (var first = await Send(user.Token, HttpMethod.Get, "/api/family-tree/map"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(1, (await Body(first)).GetProperty("data").GetProperty("pendingGeocodingMembers").GetInt32());
        }
        var failed = await WaitForGeocodeAsync(unresolvedId);
        Assert.Null(failed.Latitude); Assert.Null(failed.Longitude);
        using (var cachedFailure = await Send(user.Token, HttpMethod.Get, "/api/family-tree/map"))
        {
            var result = (await Body(cachedFailure)).GetProperty("data");
            Assert.Equal(2, result.GetProperty("totalMembers").GetInt32());
            Assert.Equal(1, result.GetProperty("locatedMembers").GetInt32());
            Assert.Equal(0, result.GetProperty("pendingGeocodingMembers").GetInt32());
        }
        Assert.Equal(initialGeocoderCalls + 2, geocoder.Calls);

        async Task<MyLife.Shared.Entities.FamilyMember> WaitForGeocodeAsync(int memberId)
        {
            var until = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < until)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var value = await scope.ServiceProvider.GetRequiredService<AppDbContext>().FamilyMembers.AsNoTracking().SingleAsync(x => x.Id == memberId);
                if (value.GeocodedAt is not null) return value;
                await Task.Delay(50);
            }
            throw new TimeoutException("Background family map geocoding did not finish.");
        }
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

    [Fact]
    public async Task Anniversary_crud_is_role_protected_and_scoped_to_the_owners_tree()
    {
        var a = await User(); var b = await User();
        var aMember = await Create(a.Token, "Ancestor A"); var bMember = await Create(b.Token, "Ancestor B");
        using (var anonymous = await client.GetAsync("/api/family-tree/anniversaries")) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var admin = await AdminToken();
        using (var adminResponse = await Send(admin, HttpMethod.Get, "/api/family-tree/anniversaries")) Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);

        var payload = Anniversary("Ancestor A memorial", aMember);
        using var created = await Send(a.Token, HttpMethod.Post, "/api/family-tree/anniversaries", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = (await Body(created)).GetProperty("data");
        var id = item.GetProperty("id").GetInt64();
        Assert.Equal(aMember, item.GetProperty("relatedMemberId").GetInt32());
        Assert.Equal("2024-02-10", item.GetProperty("originalSolarDate").GetString());
        Assert.Equal(2024, item.GetProperty("lunarYear").GetInt32());
        Assert.True(item.GetProperty("daysUntil").GetInt32() >= 0);

        using (var mismatch = await Send(a.Token, HttpMethod.Post, "/api/family-tree/anniversaries", Anniversary("Mismatch", aMember, "2024-02-11")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
            Assert.Equal("FAMILY_ANNIVERSARY_DATE_MISMATCH", (await Body(mismatch)).GetProperty("code").GetString());
        }

        using (var invalidLeap = await Send(a.Token, HttpMethod.Post, "/api/family-tree/anniversaries", Anniversary("Invalid leap", aMember, "2024-02-10", leap: true)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidLeap.StatusCode);
            Assert.Equal("FAMILY_ANNIVERSARY_INVALID_LUNAR_DATE", (await Body(invalidLeap)).GetProperty("code").GetString());
        }

        using (var preview = await Send(a.Token, HttpMethod.Get, "/api/family-tree/anniversaries/next-occurrence?day=1&month=1&isLeapMonth=false"))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var previewData = (await Body(preview)).GetProperty("data");
            Assert.Equal(1, previewData.GetProperty("lunarDay").GetInt32());
            Assert.True(previewData.GetProperty("daysUntil").GetInt32() >= 0);
        }

        using (var listA = await Send(a.Token, HttpMethod.Get, "/api/family-tree/anniversaries"))
            Assert.Equal(id, (await Body(listA)).GetProperty("data").EnumerateArray().Single().GetProperty("id").GetInt64());
        using (var listB = await Send(b.Token, HttpMethod.Get, "/api/family-tree/anniversaries"))
            Assert.Empty((await Body(listB)).GetProperty("data").EnumerateArray());
        foreach (var (method, body) in new[] { (HttpMethod.Get, (object?)null), (HttpMethod.Put, Anniversary("Tampered", bMember)), (HttpMethod.Delete, null) })
        {
            using var denied = await Send(b.Token, method, $"/api/family-tree/anniversaries/{id}", body);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        using (var foreignMember = await Send(a.Token, HttpMethod.Post, "/api/family-tree/anniversaries", Anniversary("Wrong tree", bMember)))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignMember.StatusCode);
            Assert.Equal("FAMILY_MEMBER_NOT_FOUND", (await Body(foreignMember)).GetProperty("code").GetString());
        }
        using (var invalid = await Send(a.Token, HttpMethod.Post, "/api/family-tree/anniversaries", Anniversary("x", null)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("FAMILY_ANNIVERSARY_INVALID_TITLE", (await Body(invalid)).GetProperty("code").GetString());
        }
        using (var updated = await Send(a.Token, HttpMethod.Put, $"/api/family-tree/anniversaries/{id}", Anniversary("Updated ancestor", aMember)))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal("Updated ancestor", (await Body(updated)).GetProperty("data").GetProperty("title").GetString());
        }
        using (var memberDeleted = await Send(a.Token, HttpMethod.Delete, $"/api/family-tree/{aMember}")) Assert.Equal(HttpStatusCode.OK, memberDeleted.StatusCode);
        using (var afterMemberDelete = await Send(a.Token, HttpMethod.Get, $"/api/family-tree/anniversaries/{id}"))
            Assert.Equal(JsonValueKind.Null, (await Body(afterMemberDelete)).GetProperty("data").GetProperty("relatedMemberId").ValueKind);
        using (var deleted = await Send(a.Token, HttpMethod.Delete, $"/api/family-tree/anniversaries/{id}")) Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await Send(a.Token, HttpMethod.Get, $"/api/family-tree/anniversaries/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Legacy_anniversary_without_original_dates_still_lists_and_calculates_occurrence()
    {
        var owner = await User();
        await Create(owner.Token, "Legacy member");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var treeId = await db.FamilyTrees.Where(tree => tree.OwnerUserId == owner.Id).Select(tree => tree.Id).SingleAsync();
            db.FamilyAnniversaries.Add(new MyLife.Shared.Entities.FamilyAnniversary
            {
                FamilyTreeId = treeId, EventType = "DEATH_ANNIVERSARY", Title = "Legacy", LunarDay = 1,
                LunarMonth = 1, IsLeapMonth = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var response = await Send(owner.Token, HttpMethod.Get, "/api/family-tree/anniversaries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var legacy = (await Body(response)).GetProperty("data").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, legacy.GetProperty("originalSolarDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, legacy.GetProperty("lunarYear").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(legacy.GetProperty("nextSolarDate").GetString()));
        Assert.True(legacy.GetProperty("daysUntil").GetInt32() >= 0);
    }

    private static Dictionary<string, object?> Anniversary(string title, int? memberId, string originalSolarDate = "2024-02-10", bool leap = false) => new()
    {
        ["eventType"] = "DEATH_ANNIVERSARY", ["title"] = title, ["relatedMemberId"] = memberId,
        ["lunarDay"] = 1, ["lunarMonth"] = 1, ["isLeapMonth"] = leap,
        ["originalSolarDate"] = originalSolarDate, ["lunarYear"] = 2024,
        ["place"] = "", ["host"] = null, ["notes"] = null
    };

    private async Task<string> AdminToken()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/login")
        { Content = JsonContent.Create(new { email = "integration.admin@gmail.com", password = "Admin_Test!234567" }) };
        request.Headers.Add("X-Client-Platform", "mobile");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Body(response)).GetProperty("accessToken").GetString()!;
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
