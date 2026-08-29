using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MAIMS.Data;

/// <summary>
/// Design-time factory for MaimsDbContext. Required by `dotnet ef migrations`
/// and Visual Studio's Package Manager Console (Add-Migration / Update-Database)
/// so they can construct a DbContext WITHOUT needing MAIMS.WinUI as startup project.
///
/// This factory is ONLY used at design time (migration scaffolding + database update).
/// At runtime the application uses the normal DI registration in
/// ServiceCollectionExtensions.AddMaimsData().
///
/// Connection string is read from appsettings.json in the MAIMS.Data project folder.
/// </summary>
public class MaimsDesignTimeFactory : IDesignTimeDbContextFactory<MaimsDbContext>
{
    public MaimsDbContext CreateDbContext(string[] args)
    {
        // Look for appsettings.json next to this assembly (copied to output dir).
        var basePath = AppContext.BaseDirectory;
        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var connectionString = config.GetConnectionString("MaimsDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:MaimsDb not found in MAIMS.Data/appsettings.json. " +
                "Create this file with the same connection string as MAIMS.WinUI/appsettings.json.");

        // Use a hardcoded MySQL 8.0 version instead of ServerVersion.AutoDetect().
        // AutoDetect opens a connection to MySQL during migration scaffolding,
        // which fails if MySQL is unreachable. For migration generation we
        // don't actually need to talk to the server — we just need the provider
        // to know it's MySQL 8.x so it generates correct SQL types.
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));

        var options = new DbContextOptionsBuilder<MaimsDbContext>()
            .UseMySql(connectionString, serverVersion,
                mySql => mySql.MigrationsAssembly("MAIMS.Data"))
            .Options;

        // Pass null for the interceptor — design-time migrations don't need audit logging.
        return new MaimsDbContext(options);
    }
}

