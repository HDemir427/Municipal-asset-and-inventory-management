using MAIMS.Core.Abstractions;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.Services.Audit;
using MAIMS.Services.Asset;
using MAIMS.Services.Auth;
using MAIMS.Services.Inventory;
using MAIMS.Services.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all MAIMS business services. Must be called AFTER AddMaimsData()
    /// AND after AuthService is registered by the host (Program.cs).
    /// AuthService is registered as a singleton because it doubles as the ICurrentSession
    /// holder (per-process single active user in the WinForms host).
    /// </summary>
    public static IServiceCollection AddMaimsServices(this IServiceCollection services)
    {
        // AuthService + ICurrentSession + IAuthService are registered by Program.cs
        // (host) BEFORE AddMaimsServices is called. The host owns the AuthService
        // lifetime because it needs to inject the DbContext factory closure.
        // Here we only register the business services.

        services.AddScoped<IAssetService>(sp =>
            new AssetService(
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<IAssetCodeGenerator>(),
                sp.GetRequiredService<ICurrentSession>()));

        services.AddScoped<IInventoryService>(sp =>
            new InventoryService(
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<ICurrentSession>()));

        services.AddScoped<IAuditService>(sp =>
            new AuditService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ICurrentSession>()));

        services.AddScoped<IReferenceDataService>(sp =>
            new ReferenceDataService(sp.GetRequiredService<IServiceScopeFactory>()));

        return services;
    }
}
