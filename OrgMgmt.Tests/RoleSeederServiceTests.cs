using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrgMgmt.Models;
using OrgMgmt.Services;
using Xunit;

namespace OrgMgmt.Tests;

public class RoleSeederServiceTests
{
    private static readonly string[] ExpectedRoles =
        { "Admin", "HR", "Payroll", "ScheduleManager", "Employee", "DirectManager" };

    private static (ServiceProvider provider, IConfiguration config) BuildServices(string dbName)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminSettings:Email"] = "admin@orgmgmt.local",
                ["AdminSettings:Password"] = "Admin123"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddDbContext<OrgDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 6;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireDigit = true;
            options.Password.RequireNonAlphanumeric = false;
        })
        .AddEntityFrameworkStores<OrgDbContext>()
        .AddDefaultTokenProviders();

        services.AddLogging();

        return (services.BuildServiceProvider(), config);
    }

    [Fact]
    public async Task SeedAsync_CreatesAllSixRoles()
    {
        var (provider, config) = BuildServices(nameof(SeedAsync_CreatesAllSixRoles));
        using var scope = provider.CreateScope();

        await RoleSeederService.SeedAsync(scope.ServiceProvider, config);

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in ExpectedRoles)
        {
            Assert.True(await roleManager.RoleExistsAsync(role), $"Role '{role}' should exist");
        }
    }

    [Fact]
    public async Task SeedAsync_CreatesDefaultAdminUser()
    {
        var (provider, config) = BuildServices(nameof(SeedAsync_CreatesDefaultAdminUser));
        using var scope = provider.CreateScope();

        await RoleSeederService.SeedAsync(scope.ServiceProvider, config);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync("admin@orgmgmt.local");
        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin, "Admin"));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_NoDuplicatesOnSecondRun()
    {
        var (provider, config) = BuildServices(nameof(SeedAsync_IsIdempotent_NoDuplicatesOnSecondRun));

        // Run seed twice
        using (var scope = provider.CreateScope())
        {
            await RoleSeederService.SeedAsync(scope.ServiceProvider, config);
        }
        using (var scope = provider.CreateScope())
        {
            await RoleSeederService.SeedAsync(scope.ServiceProvider, config);
        }

        // Verify still exactly 6 roles and 1 admin user
        using (var scope = provider.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var roles = await roleManager.Roles.ToListAsync();
            Assert.Equal(6, roles.Count);

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admins = await userManager.GetUsersInRoleAsync("Admin");
            Assert.Single(admins);
        }
    }
}
