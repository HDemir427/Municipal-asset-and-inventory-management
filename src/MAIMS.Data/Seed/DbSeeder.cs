using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MAIMS.Data.Seed;

/// <summary>
/// Seeds the seven built-in roles (with default permission sets), the SystemAdministrator
/// user, and a couple of departments + asset categories so the system is usable on first run.
/// Idempotent — safe to call on every application start.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(DbContext ctx, string adminPasswordHash, CancellationToken ct = default)
    {
        await ctx.Database.EnsureCreatedAsync(ct);

        // Departments — realistic municipal departments
        if (!await ctx.Set<Department>().AnyAsync(ct))
        {
            ctx.Set<Department>().AddRange(
                new Department { Name = "Mayor's Office", Code = "MOF" },
                new Department { Name = "City Clerk", Code = "CLK" },
                new Department { Name = "Finance Department", Code = "FIN" },
                new Department { Name = "Public Works Department", Code = "PWD" },
                new Department { Name = "Parks and Recreation Department", Code = "PRD" },
                new Department { Name = "Police Department", Code = "POL" },
                new Department { Name = "Fire Department", Code = "FIR" },
                new Department { Name = "Building and Safety", Code = "BSF" },
                new Department { Name = "Planning and Development", Code = "PLN" },
                new Department { Name = "Water and Sewer Department", Code = "WSD" },
                new Department { Name = "Sanitation Department", Code = "SAN" },
                new Department { Name = "Library Services", Code = "LIB" },
                new Department { Name = "Information Technology Department", Code = "ITD" },
                new Department { Name = "Human Resources Department", Code = "HRD" },
                new Department { Name = "City Attorney's Office", Code = "CAT" },
                new Department { Name = "Code Enforcement", Code = "COD" },
                new Department { Name = "Central Stores / Warehouse", Code = "WHS" }
            );
            await ctx.SaveChangesAsync(ct);
        }

        // Roles + permissions — UPSERT: create if missing, UPDATE permissions if role exists.
        // This ensures that when Permissions.cs is updated (e.g., new permissions added),
        // the DB roles automatically get the latest permission set on next app start.
        foreach (var kvp in Permissions.DefaultRolePermissions)
        {
            var existingRole = await ctx.Set<Role>().FirstOrDefaultAsync(r => r.Name == kvp.Key, ct);
            var newPermsJson = System.Text.Json.JsonSerializer.Serialize(kvp.Value);

            if (existingRole is null)
            {
                // Role doesn't exist — create it.
                ctx.Set<Role>().Add(new Role
                {
                    Name = kvp.Key,
                    Description = $"{kvp.Key} role",
                    PermissionsJson = newPermsJson
                });
            }
            else
            {
                // Role exists — update its permissions to the latest from Permissions.cs.
                // Only update system roles (the 7 built-in ones). Custom roles are left alone.
                if (Permissions.SystemRoles.Contains(existingRole.Name))
                {
                    existingRole.PermissionsJson = newPermsJson;
                    existingRole.Description = $"{kvp.Key} role";
                }
            }
        }
        await ctx.SaveChangesAsync(ct);

        // Asset categories
        if (!await ctx.Set<AssetCategory>().AnyAsync(ct))
        {
            foreach (AssetCategoryType t in Enum.GetValues(typeof(AssetCategoryType)))
            {
                ctx.Set<AssetCategory>().Add(new AssetCategory
                {
                    Name = t.ToString(),
                    CategoryType = t,
                    DepreciationMethod = t == AssetCategoryType.Land ? "NONE" : "STRAIGHT_LINE",
                    UsefulLifeYears = t switch
                    {
                        AssetCategoryType.Buildings => 40,
                        AssetCategoryType.Infrastructure => 30,
                        AssetCategoryType.Vehicles => 8,
                        AssetCategoryType.Equipment => 10,
                        AssetCategoryType.FurnitureAndFixtures => 10,
                        AssetCategoryType.ITHardware => 4,
                        AssetCategoryType.Software => 3,
                        _ => null
                    }
                });
            }
            await ctx.SaveChangesAsync(ct);
        }

        // Admin user — assign to Information Technology Department (Code = "ITD")
        var adminRole = await ctx.Set<Role>().FirstOrDefaultAsync(r => r.Name == "SystemAdministrator", ct);
        var itDept = await ctx.Set<Department>().FirstOrDefaultAsync(d => d.Code == "ITD", ct);
        if (adminRole is not null && itDept is not null
            && !await ctx.Set<User>().AnyAsync(u => u.Username == "admin", ct))
        {
            ctx.Set<User>().Add(new User
            {
                Name = "System Administrator",
                Email = "admin@maims.local",
                Username = "admin",
                RoleId = adminRole.Id,
                DepartmentId = itDept.Id,
                Status = UserStatus.Active,
                PasswordHash = adminPasswordHash
            });
            await ctx.SaveChangesAsync(ct);
        }
    }
}
