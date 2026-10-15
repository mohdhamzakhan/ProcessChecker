using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace ProcessChecker.Contracts;

/// Applies EF migrations at startup. The services share one schema, so each has its own history table.
public static class DbMigrator
{
    public static string HistoryTable(string service) => "PC_MIG_" + service.ToUpperInvariant();

    /// firstTable: a table every older installation of this service already has.
    /// legacyUpgrade: brings an old installation (made before migrations) up to the shape of the first migration.
    public static async Task RunAsync(DbContext db, string firstTable, Func<DbConnection, Task>? legacyUpgrade, ILogger log)
    {
        var name = db.GetType().Name;
        var all = db.Database.GetMigrations().ToList();
        if (all.Count == 0)
            throw new InvalidOperationException(
                $"{name} has no migrations yet. Create the first one: dotnet ef migrations add Initial --context {name}");

        await db.Database.OpenConnectionAsync();
        try
        {
            var conn = db.Database.GetDbConnection();
            var history = db.GetService<IHistoryRepository>();

            if (!await history.ExistsAsync() && await DbObjects.TableExistsAsync(conn, firstTable))
            {
                log.LogWarning("{Context}: the tables exist but have no migration history. Adopting them as migration {Migration}.",
                    name, all[0]);
                if (legacyUpgrade != null) await legacyUpgrade(conn);
                await db.Database.ExecuteSqlRawAsync(history.GetCreateScript());
                await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(all[0], ProductVersion())));
            }

            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
                log.LogInformation("{Context}: applying {Count} migration(s): {Names}", name, pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static string ProductVersion()
    {
        var v = typeof(DbContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "8.0.0";
        v = v.Split('+')[0];
        return v.Length > 32 ? v[..32] : v;
    }
}