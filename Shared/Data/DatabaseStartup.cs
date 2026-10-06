using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace MyLife.Shared.Data;

public static class DatabaseStartup
{
    public static async Task MigrateAndSeedAsync(AppDbContext db, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await BaselineLegacySchemaAsync(db, configuration, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await db.SeedDataAsync(configuration);
    }

    private static async Task BaselineLegacySchemaAsync(AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        try
        {
            if (shouldClose) await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT c.relname, a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_attribute a ON a.attrelid = c.oid
                WHERE n.nspname = current_schema() AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped;
                """;
            var columns = new Dictionary<(string Table, string Column), (string Type, bool Required)>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) columns[(reader.GetString(0), reader.GetString(1))] = (reader.GetString(2), reader.GetBoolean(3));
            if (columns.Keys.Any(x => x.Table == "__EFMigrationsHistory")) return;
            var entities = db.Model.GetEntityTypes().ToList();
            if (!entities.Any(e => columns.Keys.Any(x => x.Table == e.GetTableName()))) return;
            if (!configuration.GetValue<bool>("Database:AllowLegacyBaseline"))
                throw new InvalidOperationException("A legacy database without migration history was detected. Back up the database and explicitly enable Database:AllowLegacyBaseline for this upgrade.");

            foreach (var entity in entities)
            {
                var table = entity.GetTableName()!;
                var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
                foreach (var property in entity.GetProperties())
                {
                    var column = property.GetColumnName(store)!;
                    if (table == "users" && column is "google_subject" or "has_local_provider" or "has_google_provider" or
                        "avatar_drive_file_id" or "avatar_source" or "google_avatar_source_url")
                    {
                        if (columns.ContainsKey((table, column))) throw new InvalidOperationException("Legacy schema already contains upgrade columns but has no migration history; review it manually.");
                        continue;
                    }
                    var expectedType = property.GetColumnType() ?? property.GetRelationalTypeMapping().StoreType;
                    if (!columns.TryGetValue((table, column), out var actual) || actual.Type != expectedType || actual.Required == property.IsNullable)
                        throw new InvalidOperationException($"Legacy schema does not match the baseline at {table}.{column}. No migration history was changed.");
                }
            }
            // Require original unique keys and foreign keys, not just similarly named tables.
            command.CommandText = """
                SELECT c.relname, string_agg(a.attname, ',' ORDER BY keys.ordinality)
                FROM pg_index i JOIN pg_class c ON c.oid = i.indrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY keys(attnum, ordinality)
                JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = keys.attnum
                WHERE n.nspname = current_schema() AND i.indisunique AND i.indisvalid GROUP BY c.relname, i.indexrelid;
                """;
            var uniqueKeys = new HashSet<(string Table, string Columns)>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) uniqueKeys.Add((reader.GetString(0), reader.GetString(1)));
            foreach (var entity in entities)
            {
                var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                var requiredKeys = entity.GetKeys().Select(k => k.Properties).Concat(entity.GetIndexes().Where(i => i.IsUnique && !i.Properties.Any(p => p.Name == "GoogleSubject")).Select(i => i.Properties));
                foreach (var key in requiredKeys)
                    if (!uniqueKeys.Contains((store.Name, string.Join(',', key.Select(p => p.GetColumnName(store))))))
                        throw new InvalidOperationException($"Legacy schema is missing a required unique key on {store.Name}. No migration history was changed.");
            }
            command.CommandText = """
                SELECT c.relname, con.conname FROM pg_constraint con JOIN pg_class c ON c.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND con.contype = 'f' AND con.convalidated;
                """;
            var foreignKeys = new HashSet<(string Table, string Name)>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) foreignKeys.Add((reader.GetString(0), reader.GetString(1)));
            foreach (var entity in entities)
                foreach (var key in entity.GetForeignKeys())
                    if (!foreignKeys.Contains((entity.GetTableName()!, key.GetConstraintName()!)))
                        throw new InvalidOperationException("Legacy schema is missing a required foreign key. No migration history was changed.");

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741003003);", ct);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" character varying(32) NOT NULL
                );
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260921072914_AddFamilyRelationships', '10.0.11') ON CONFLICT ("MigrationId") DO NOTHING;
                """, ct);
            await transaction.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000")
        {
            // Only a missing database is delegated to MigrateAsync; permission/authentication failures remain fatal.
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}
