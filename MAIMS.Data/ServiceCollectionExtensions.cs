using MAIMS.Core.Abstractions;
using MAIMS.Core.Interfaces;
using MAIMS.Data.AssetCodeGeneration;
using MAIMS.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MaimsDbContext with Pomelo MySQL provider, connection pooling,
    /// audit interceptor, UnitOfWork, generic repository, and the asset code generator.
    ///
    /// NOTE: ICurrentSession is NOT registered here — the host (Program.cs) owns it
    /// because the session lifecycle depends on login state. The host must register
    /// ICurrentSession BEFORE calling AddMaimsData, OR pass a pre-login fallback
    /// via the `session` parameter for seeding-only use.
    /// </summary>
    public static IServiceCollection AddMaimsData(
        this IServiceCollection services,
        string connectionString,
        ICurrentSession session)
    {
        // AuditWriter is a singleton queue; the interceptor pulls it via Lazy<T>.
        var auditWriter = new AuditQueueWriter();
        services.AddSingleton<IAuditWriter>(auditWriter);

        // Register the session as a singleton fallback for early-stage operations
        // (DbSeeder runs before login). The host (Program.cs) will override
        // ICurrentSession with AuthService after login — that's why we register
        // the concrete type (NullCurrentSession) AND ICurrentSession. The last
        // registration wins in DI, so the host's later registration takes precedence.
        // We do NOT re-register ICurrentSession here to avoid clobbering the host.
        services.AddSingleton(session.GetType(), session);

        services.AddSingleton<AuditSaveChangesInterceptor>(sp =>
            new AuditSaveChangesInterceptor(
                sp.GetRequiredService<ICurrentSession>(),
                new Lazy<IAuditWriter>(() => sp.GetRequiredService<IAuditWriter>())));

        services.AddDbContextPool<MaimsDbContext>((sp, opts) =>
        {
            opts.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
                mySql =>
                {
                    mySql.MigrationsAssembly("MAIMS.Data");
                })
                .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>())
                .EnableDetailedErrors()
                .EnableSensitiveDataLogging(); // <-- remove in production
        }, poolSize: 128);

        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MaimsDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repositories.Repository<>));
        services.AddScoped<IAssetCodeGenerator, SequentialAssetCodeGenerator>();

        return services;
    }
}
