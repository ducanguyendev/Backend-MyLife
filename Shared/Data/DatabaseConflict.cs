namespace MyLife.Shared.Data;

public static class DatabaseConflict
{
    // Execution strategies may add an InvalidOperationException outside the
    // DbUpdateException. Preserve the existing HTTP409 classification through it.
    public static bool IsConflict(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is Npgsql.PostgresException { SqlState: "40001" or "40P01" or "23505" or "23503" or "23001" }) return true;
        return false;
    }
}
