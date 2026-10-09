using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using MyLife.Shared.Data;
using Npgsql;
using Xunit;

public sealed class FamilyTreeMigrationTests
{
    [Fact]
    public async Task Empty_member_upgrade_is_non_nullable_restricted_unseeded_and_reversible_without_library_changes()
    {
        var schema = $"tree_migration_{Guid.NewGuid():N}";
        await using var setup = new NpgsqlConnection(MyLifeFactory.ConnectionString); await setup.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup).ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(MyLifeFactory.ConnectionString) { SearchPath = schema }.ConnectionString;
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
            var migrator = db.GetService<IMigrator>(); const string previous = "20261006090341_AddLibraryCategories";
            await migrator.MigrateAsync(previous);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO users (id,email,password_hash,auth_provider,is_active,created_at,updated_at) SELECT n, 'migration.' || n || '@gmail.com','test-only',0,true,now(),now() FROM generate_series(1,4) n; INSERT INTO user_roles(user_id,role_id) SELECT n, (SELECT id FROM roles WHERE name=CASE WHEN n=1 THEN 'ADMIN' ELSE 'USER' END) FROM generate_series(1,4) n;");
            Assert.Equal(0, await db.FamilyMembers.CountAsync());
            await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
            async Task<string> LibrarySchema() {
                var result = new List<string>(); await using var command = new NpgsqlCommand("SELECT table_name,column_name,data_type,is_nullable,coalesce(column_default,'') FROM information_schema.columns WHERE table_schema=current_schema() AND table_name LIKE 'library_%' ORDER BY table_name,ordinal_position", sql);
                await using var rows = await command.ExecuteReaderAsync(); while (await rows.ReadAsync()) result.Add(string.Join('|', Enumerable.Range(0,5).Select(rows.GetString))); return string.Join('\n', result);
            }
            var libraryBefore = await LibrarySchema();
            // Production default refuses pending schema without applying migrations or seed.
            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseStartup.InitializeAsync(db, new ConfigurationBuilder().Build(), false));
            Assert.Equal(previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
            await migrator.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal(4, await db.Users.CountAsync()); Assert.Equal(0, await db.FamilyTrees.CountAsync());
            Assert.Equal(libraryBefore, await LibrarySchema());
            await using (var columns = new NpgsqlCommand("SELECT is_nullable,column_default FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='family_members' AND column_name='family_tree_id'", sql))
            { await using var row = await columns.ExecuteReaderAsync(); Assert.True(await row.ReadAsync()); Assert.Equal("NO", row.GetString(0)); Assert.True(row.IsDBNull(1)); }
            await using (var indexes = new NpgsqlCommand("SELECT indexname FROM pg_indexes WHERE schemaname=current_schema() AND tablename IN ('family_trees','family_members')", sql))
            { var names = new HashSet<string>(); await using var rows = await indexes.ExecuteReaderAsync(); while(await rows.ReadAsync()) names.Add(rows.GetString(0)); Assert.Contains("IX_family_trees_owner_user_id",names); Assert.Contains("IX_family_members_family_tree_id",names); Assert.Contains("IX_family_members_family_tree_id_generation",names); }
            await new NpgsqlCommand("INSERT INTO family_trees(owner_user_id,created_at,updated_at) VALUES(2,now(),now())", sql).ExecuteNonQueryAsync();
            async Task Reject(string text,string state) { var error = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(text,sql).ExecuteNonQueryAsync()); Assert.Equal(state,error.SqlState); }
            await Reject("INSERT INTO family_trees(owner_user_id,created_at,updated_at) VALUES(2,now(),now())","23505");
            await Reject("INSERT INTO family_members(full_name,generation,created_at,updated_at) VALUES('Invalid',1,now(),now())","23502");
            await Reject("INSERT INTO family_members(full_name,generation,created_at,updated_at,family_tree_id) VALUES('Invalid',1,now(),now(),0)","23503");
            await Reject("DELETE FROM users WHERE id=2","23001");
            await DatabaseStartup.InitializeAsync(db,new ConfigurationBuilder().Build(),false);
            Assert.Equal(4,await db.Users.CountAsync());
            await migrator.MigrateAsync(previous);
            Assert.True((await new NpgsqlCommand("SELECT to_regclass('family_trees')::text",sql).ExecuteScalarAsync()) is null or DBNull);
            Assert.Equal(libraryBefore,await LibrarySchema()); Assert.Equal(4,await db.Users.CountAsync());
            Assert.Equal(0L,(long)(await new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='family_members' AND column_name='family_tree_id'",sql).ExecuteScalarAsync())!);
        }
        finally
        {
            if(!schema.StartsWith("tree_migration_",StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test schema.");
            await new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",setup).ExecuteNonQueryAsync();
        }
    }
}
