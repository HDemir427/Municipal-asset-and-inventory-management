using MAIMS.Core.Abstractions;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.Data.Seed;
using MAIMS.Services;
using MAIMS.Services.Auth;
using MAIMS.WinUI.Forms;
using MAIMS.WinUI.Theming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Settings.Configuration;

namespace MAIMS.WinUI;

internal static class Program
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Pre-login session — used during startup (seeding) before any user logs in.
    /// AuthService takes over as ICurrentSession once login succeeds.
    /// </summary>
    public static readonly NullCurrentSession PreLogin = NullCurrentSession.Instance;

    [STAThread]
    private static void Main()
    {
        // High-DPI awareness — required by spec §10.
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Apply MAIMS-branded renderer globally to all ToolStrip / MenuStrip /
        // StatusStrip instances. This forces white text on our dark blue brand
        // color — the default renderer ignores BackColor/ForeColor and paints
        // with OS theme colors, leaving text unreadable.
        ToolStripManager.Renderer = new MaimsToolStripRenderer();

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services, config);
            Services = services.BuildServiceProvider(validateScopes: true);

            // Create schema (if not exists) + seed reference data (idempotent).
            // DbSeeder.SeedAsync calls EnsureCreatedAsync first (creates all tables
            // on first run), then seeds 17 departments, 7 roles, 8 asset categories,
            // and the bootstrap admin user. Safe to call on every startup — seeding
            // checks for existing data before inserting.
            using (var scope = Services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
                DbSeeder.SeedAsync(ctx, AuthService.HashPassword("Admin@123"), CancellationToken.None)
                    .GetAwaiter().GetResult();
            }

            // Login loop — supports sign-out (returns to LoginForm) vs exit.
            var auth = Services.GetRequiredService<AuthService>();
            while (true)
            {
                using var loginForm = new LoginForm(auth);
                var result = loginForm.ShowDialog();
                if (result != DialogResult.OK)
                {
                    return; // user closed login → exit app
                }

                // Run MainForm. When it closes, check if it was a sign-out
                // (user wants to go back to login) or a real exit.
                var mainForm = new MainForm(Services);
                Application.Run(mainForm);

                if (mainForm.IsSignOut)
                {
                    // User signed out — log them out and loop back to LoginForm.
                    auth.LogoutAsync().GetAwaiter().GetResult();
                    continue;  // go back to top of while loop → show LoginForm
                }

                // User closed or exited — terminate.
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "MAIMS terminated unexpectedly.");
            MessageBox.Show($"Fatal error: {ex.Message}\n\nSee logs/maims-.log for details.",
                "MAIMS — Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        // Register IConfiguration so forms can resolve it (e.g. AuditLogViewerForm
        // reads the connection string for the Purge workflow).
        services.AddSingleton<IConfiguration>(config);

        var connectionString = config.GetConnectionString("MaimsDb")
            ?? throw new InvalidOperationException("ConnectionStrings:MaimsDb is missing.");

        // Register a pre-login ICurrentSession FIRST. This is used during seeding
        // (before any user logs in) and as the initial ICurrentSession binding that
        // AddMaimsData's AuditSaveChangesInterceptor depends on.
        services.AddSingleton<ICurrentSession>(PreLogin);

        // Register MaimsDbContext + IUnitOfWork + repositories + audit interceptor.
        services.AddMaimsData(connectionString, PreLogin);

        // AuthService doubles as ICurrentSession after login. Singleton lifetime.
        // Uses IServiceScopeFactory to create fresh DI scopes for each operation
        // (singleton cannot directly consume scoped MaimsDbContext).
        services.AddSingleton<AuthService>();
        services.AddSingleton<ICurrentSession>(sp => sp.GetRequiredService<AuthService>());
        services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<AuthService>());

        // Business services.
        services.AddMaimsServices();

        // Forms (transient — created on demand).
        services.AddTransient<AssetListForm>();
        services.AddTransient<AssetEditForm>();
    }
}
