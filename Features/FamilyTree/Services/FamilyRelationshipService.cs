using Microsoft.EntityFrameworkCore;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;

namespace MyLife.Features.FamilyTree.Services;

public sealed class FamilyRelationshipService(AppDbContext db, IFamilyTreeService trees) : IFamilyRelationshipService
{
    private const int MaxDepth = 10;

    public async Task<FamilyRelationshipResultDto?> FindAsync(int userId, int fromMemberId, int toMemberId, CancellationToken ct)
    {
        var tree = await trees.EnsureUserFamilyTreeAsync(userId, ct);
        var members = await db.FamilyMembers.AsNoTracking().Where(m => m.FamilyTreeId == tree.Id)
            .Select(m => new Node(m.Id, m.FullName, m.Gender, m.DateOfBirth, m.FatherId, m.MotherId, m.SpouseId, m.Generation))
            .ToDictionaryAsync(m => m.Id, ct);
        if (!members.TryGetValue(fromMemberId, out var from) || !members.TryGetValue(toMemberId, out var to)) return null;

        var explicitRelations = await db.FamilyRelationships.AsNoTracking()
            .Where(r => r.Member1!.FamilyTreeId == tree.Id && r.Member2!.FamilyTreeId == tree.Id)
            .Select(r => new Relation(r.Member1Id, r.Member2Id, r.RelationType)).ToListAsync(ct);
        var graph = BuildGraph(members, explicitRelations);
        var route = FindPath(graph, fromMemberId, toMemberId);
        var (code, label) = Classify(route, members);
        var (reverseCode, reverseLabel) = Classify(route.Reverse().ToArray(), members);
        var path = route.Select((id, index) => new FamilyRelationshipPathNodeDto(id, members[id].Name,
            index == 0 ? null : EdgeLabel(route[index - 1], id, members))).ToArray();
        return new(fromMemberId, toMemberId, code, label, reverseCode, reverseLabel, route.Length - 1, path);
    }

    private static Dictionary<int, HashSet<int>> BuildGraph(IReadOnlyDictionary<int, Node> members, IEnumerable<Relation> relations)
    {
        var graph = members.Keys.ToDictionary(id => id, _ => new HashSet<int>());
        void Link(int a, int b) { if (a != b && graph.ContainsKey(a) && graph.ContainsKey(b)) { graph[a].Add(b); graph[b].Add(a); } }
        foreach (var member in members.Values)
        {
            if (member.FatherId is int father) Link(member.Id, father);
            if (member.MotherId is int mother) Link(member.Id, mother);
            if (member.SpouseId is int spouse) Link(member.Id, spouse);
        }
        foreach (var relation in relations)
            if (string.Equals(relation.RelationType, "sibling", StringComparison.OrdinalIgnoreCase))
                Link(relation.Member1Id, relation.Member2Id);
        // Same known biological parent is the established sibling rule (OR, not AND).
        var ids = members.Keys.ToArray();
        for (var i = 0; i < ids.Length; i++) for (var j = i + 1; j < ids.Length; j++)
        {
            var a = members[ids[i]]; var b = members[ids[j]];
            if (a.FatherId.HasValue && a.FatherId == b.FatherId || a.MotherId.HasValue && a.MotherId == b.MotherId) Link(a.Id, b.Id);
        }
        return graph;
    }

    private static int[] FindPath(Dictionary<int, HashSet<int>> graph, int from, int to)
    {
        if (from == to) return [from];
        var queue = new Queue<int>(); queue.Enqueue(from);
        var parent = new Dictionary<int, int?> { [from] = null };
        var depth = new Dictionary<int, int> { [from] = 0 };
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (depth[current] >= MaxDepth) continue;
            foreach (var next in graph[current].Order())
            {
                if (!parent.TryAdd(next, current)) continue;
                depth[next] = depth[current] + 1;
                if (next == to)
                {
                    var path = new List<int> { to };
                    for (var cursor = current; ; cursor = parent[cursor]!.Value) { path.Add(cursor); if (cursor == from) break; }
                    path.Reverse(); return path.ToArray();
                }
                queue.Enqueue(next);
            }
        }
        return [];
    }

    private static (string Code, string Label) Classify(int[] path, IReadOnlyDictionary<int, Node> members)
    {
        if (path.Length == 0) return ("UNKNOWN", "Unknown relationship");
        if (path.Length == 1) return ("SELF", "Self");
        var nodes = path.Select(id => members[id]).ToArray();
        var edges = Enumerable.Range(1, nodes.Length - 1).Select(i => Edge(nodes[i - 1], nodes[i])).ToArray();
        var target = nodes[^1];
        if (edges.Length == 1)
        {
            if (edges[0] == "SPOUSE") return Gender(target) switch { "M" => ("HUSBAND", "Husband"), "F" => ("WIFE", "Wife"), _ => ("SPOUSE", "Spouse") };
            if (edges[0] == "SIBLING") return SiblingLabel(nodes[0], target);
            if (edges[0] == "PARENT") return Gender(target) switch { "M" => ("FATHER", "Father"), "F" => ("MOTHER", "Mother"), _ => ("PARENT", "Parent") };
            if (edges[0] == "CHILD") return Gender(target) switch { "M" => ("SON", "Son"), "F" => ("DAUGHTER", "Daughter"), _ => ("CHILD", "Child") };
        }
        if (edges.All(e => e == "PARENT"))
        {
            var code = edges.Length == 2 ? "GRAND" : edges.Length == 3 ? "GREAT_GRAND" : "";
            if (code.Length > 0) return Gender(target) switch { "M" => ($"{code}FATHER", edges.Length == 2 ? "Grandfather" : "Great-grandfather"), "F" => ($"{code}MOTHER", edges.Length == 2 ? "Grandmother" : "Great-grandmother"), _ => ("ANCESTOR", "Ancestor") };
            return ("ANCESTOR", "Ancestor");
        }
        if (edges.All(e => e == "CHILD")) return edges.Length == 2 ? Gender(target) switch { "M" => ("GRANDSON", "Grandson"), "F" => ("GRANDDAUGHTER", "Granddaughter"), _ => ("DESCENDANT", "Descendant") } : ("DESCENDANT", "Descendant");
        if (edges.Length == 2 && edges[0] == "PARENT" && edges[1] == "SIBLING")
        {
            var side = Gender(target) == "M" ? (nodes[0].MotherId == nodes[1].Id ? "MATERNAL_UNCLE" : "PATERNAL_UNCLE") : "AUNT";
            return side switch { "MATERNAL_UNCLE" => (side, "Maternal uncle"), "PATERNAL_UNCLE" => (side, "Paternal uncle"), _ => ("AUNT", "Aunt") };
        }
        if (edges.Length == 2 && edges[0] == "SIBLING" && edges[1] == "CHILD")
            return Gender(target) switch { "M" => ("NEPHEW", "Nephew"), "F" => ("NIECE", "Niece"), _ => ("RELATED", "Relative") };
        if (edges.Length == 3 && (edges[0] == "PARENT" && edges[1] == "SIBLING" && edges[2] == "CHILD" ||
            edges[0] == "CHILD" && edges[1] == "SIBLING" && edges[2] == "PARENT")) return ("COUSIN", "Cousin");
        if (edges.Length == 3 && edges[0] == "PARENT" && edges[1] == "PARENT")
            return Gender(target) switch { "M" => ("GRANDFATHER", "Grandfather"), "F" => ("GRANDMOTHER", "Grandmother"), _ => ("ANCESTOR", "Ancestor") };
        if (edges.Length == 3 && edges[0] == "CHILD" && edges[1] == "CHILD") return Gender(target) switch { "M" => ("GRANDSON", "Grandson"), "F" => ("GRANDDAUGHTER", "Granddaughter"), _ => ("DESCENDANT", "Descendant") };
        return ("RELATED", "Relative");
    }

    private static (string, string) SiblingLabel(Node self, Node other)
    {
        var shared = self.FatherId.HasValue && self.FatherId == other.FatherId || self.MotherId.HasValue && self.MotherId == other.MotherId;
        if (!shared) return ("COUSIN", "Cousin");
        var gender = Gender(other);
        if (self.Birth is null || other.Birth is null || self.Birth == other.Birth) return ("SIBLING", "Sibling");
        var prefix = other.Birth < self.Birth ? "OLDER" : "YOUNGER";
        return gender switch { "M" => ($"{prefix}_BROTHER", prefix == "OLDER" ? "Older brother" : "Younger brother"), "F" => ($"{prefix}_SISTER", prefix == "OLDER" ? "Older sister" : "Younger sister"), _ => ("SIBLING", "Sibling") };
    }

    private static string EdgeLabel(int from, int to, IReadOnlyDictionary<int, Node> members) => Edge(members[from], members[to]);
    private static string Edge(Node from, Node to)
    {
        if (from.SpouseId == to.Id || to.SpouseId == from.Id) return "SPOUSE";
        if (to.FatherId == from.Id || to.MotherId == from.Id) return "CHILD";
        if (from.FatherId == to.Id || from.MotherId == to.Id) return "PARENT";
        return "SIBLING";
    }
    private static string Gender(Node n) => n.Gender?.Trim().ToLowerInvariant() switch { "male" or "m" or "nam" => "M", "female" or "f" or "nữ" or "nu" => "F", _ => "" };
    private sealed record Relation(int Member1Id, int Member2Id, string RelationType);
    private sealed record Node(int Id, string Name, string? Gender, DateOnly? Birth, int? FatherId, int? MotherId, int? SpouseId, int Generation);
}
