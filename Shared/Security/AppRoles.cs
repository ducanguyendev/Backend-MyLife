namespace MyLife.Shared.Security;

public static class AppRoles
{
    public const string Admin = "ADMIN";
    public const string User = "USER";

    public static string? ExclusiveRole(IEnumerable<string> roles)
    {
        var assigned = roles.Where(r => r is Admin or User).Distinct().ToArray();
        return assigned.Length == 1 ? assigned[0] : null;
    }
}
