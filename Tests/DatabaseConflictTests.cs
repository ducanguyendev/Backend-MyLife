using Microsoft.EntityFrameworkCore;
using MyLife.Shared.Data;
using Npgsql;
using Xunit;

public sealed class DatabaseConflictTests
{
    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    [InlineData("23505")]
    [InlineData("23503")]
    [InlineData("23001")]
    public void Existing_conflict_states_survive_execution_strategy_wrappers(string sqlState)
    {
        var postgres = new PostgresException("Synthetic conflict", "ERROR", "ERROR", sqlState);
        Assert.True(DatabaseConflict.IsConflict(postgres));
        Assert.True(DatabaseConflict.IsConflict(new DbUpdateException("Synthetic update", postgres)));
        Assert.True(DatabaseConflict.IsConflict(new InvalidOperationException("Synthetic execution strategy", new DbUpdateException("Synthetic update", postgres))));
    }

    [Fact]
    public void Unrelated_errors_and_database_authentication_failures_are_not_conflicts()
    {
        Assert.False(DatabaseConflict.IsConflict(null));
        Assert.False(DatabaseConflict.IsConflict(new InvalidOperationException("Synthetic failure")));
        Assert.False(DatabaseConflict.IsConflict(new InvalidOperationException("Synthetic failure", new PostgresException("Synthetic auth", "ERROR", "ERROR", "28000"))));
    }
}
