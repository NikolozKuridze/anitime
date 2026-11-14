using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Anitime.Api.Tests;

/// <summary>The production server can't install PostgreSQL extensions, so the schema must never need one.</summary>
[Collection(ApiCollection.Name)]
public sealed class DatabaseExtensionTests(ApiFixture fixture)
{
    [Fact]
    public async Task AllMigrations_RunOnAPlainPostgres_WithoutCreatingAnyExtension()
    {
        List<string> extensions = [];
        List<string> pending = [];
        await fixture.WithDbAsync(async db =>
        {
            extensions = await db.Database.SqlQuery<string>($"SELECT extname AS \"Value\" FROM pg_extension").ToListAsync();
            pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        });

        Assert.Empty(pending); // the fixture applied every migration to a fresh postgres:16-alpine
        Assert.Equal(["plpgsql"], extensions); // plpgsql is built in and always present
    }

    [Fact]
    public void NoMigrationOrModelMentionsAnExtension()
    {
        var migrations = typeof(Anitime.Infrastructure.Persistence.AppDbContext).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Microsoft.EntityFrameworkCore.Migrations.Migration)))
            .Select(t => (Microsoft.EntityFrameworkCore.Migrations.Migration)Activator.CreateInstance(t)!)
            .ToList();

        Assert.NotEmpty(migrations);
        foreach (var migration in migrations)
        {
            var sql = string.Join("\n", migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Select(o => o.Sql));
            Assert.DoesNotContain("EXTENSION", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(migration.UpOperations, o => o.GetAnnotations().Any(a => a.Name.Contains("PostgresExtension", StringComparison.Ordinal)));
        }

        using var scope = fixture.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<Anitime.Infrastructure.Persistence.AppDbContext>().Model;
        Assert.DoesNotContain(model.GetAnnotations(), a => a.Name.StartsWith("Npgsql:PostgresExtension", StringComparison.Ordinal));
    }
}
