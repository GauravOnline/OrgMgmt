using System.Security.Claims;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrgMgmt;
using OrgMgmt.Models;
using OrgMgmt.Services;

namespace OrgMgmt.Tests.Properties;

// ──────────────────────────────────────────────────────────────────────
// Feature: role-based-authentication, Property 2: Invalid passwords are rejected
// ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Generates passwords that violate at least one Identity complexity rule:
/// min 6 chars, at least one uppercase, one lowercase, one digit.
/// </summary>
public static class InvalidPasswordArb
{
    public static Arbitrary<string> Generate()
    {
        // Strategy: pick one violation category, then build a string that has that flaw.
        var gen =
            from violationType in Gen.Choose(0, 3)
            from password in violationType switch
            {
                0 => TooShort(),       // fewer than 6 characters
                1 => NoUppercase(),    // no uppercase letter
                2 => NoLowercase(),    // no lowercase letter
                _ => NoDigit()         // no digit
            }
            select password;

        return gen.ToArbitrary();
    }

    // 1-5 chars from valid char pool (may still miss other rules, but guaranteed too short)
    private static Gen<string> TooShort() =>
        from len in Gen.Choose(1, 5)
        from chars in Gen.Elements(
            'a', 'b', 'c', 'A', 'B', 'C', '1', '2', '3'
        ).ListOf(len)
        select new string(chars.ToArray());

    // 6-20 chars, lowercase + digits only (no uppercase)
    private static Gen<string> NoUppercase() =>
        from len in Gen.Choose(6, 20)
        from chars in Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', '1', '2', '3'
        ).ListOf(len)
        select new string(chars.ToArray());

    // 6-20 chars, uppercase + digits only (no lowercase)
    private static Gen<string> NoLowercase() =>
        from len in Gen.Choose(6, 20)
        from chars in Gen.Elements(
            'A', 'B', 'C', 'D', 'E', 'F', '1', '2', '3'
        ).ListOf(len)
        select new string(chars.ToArray());

    // 6-20 chars, uppercase + lowercase only (no digit)
    private static Gen<string> NoDigit() =>
        from len in Gen.Choose(6, 20)
        from chars in Gen.Elements(
            'a', 'b', 'c', 'A', 'B', 'C', 'x', 'Y', 'z'
        ).ListOf(len)
        select new string(chars.ToArray());
}

/// <summary>
/// For any password that violates at least one complexity requirement,
/// attempting to create a user with that password should fail.
/// **Validates: Requirements 2.3**
/// </summary>
public class InvalidPasswordsRejectedPropertyTests
{
    private static ServiceProvider BuildServices(string dbName)
    {
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
        return services.BuildServiceProvider();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InvalidPasswordArbProvider) })]
    public bool InvalidPassword_IsRejected(string invalidPassword)
    {
        var dbName = $"InvalidPwdTest_{Guid.NewGuid()}";
        using var provider = BuildServices(dbName);
        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = $"user_{Guid.NewGuid()}@test.com",
            Email = $"user_{Guid.NewGuid()}@test.com"
        };

        var result = userManager.CreateAsync(user, invalidPassword).GetAwaiter().GetResult();
        return !result.Succeeded;
    }
}

/// <summary>
/// Wrapper class to provide the InvalidPasswordArb as an Arbitrary provider for FsCheck.
/// </summary>
public class InvalidPasswordArbProvider
{
    public static Arbitrary<string> String() => InvalidPasswordArb.Generate();
}

// ──────────────────────────────────────────────────────────────────────
// Feature: role-based-authentication, Property 7: Role seeder idempotence
// ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Running RoleSeederService.SeedAsync N times (N from 1 to 5) produces
/// exactly 6 roles and exactly 1 admin user — the same state as running it once.
/// **Validates: Requirements 5.3, 5.4**
/// </summary>
public class RoleSeederIdempotencePropertyTests
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

    [Property(MaxTest = 100)]
    public bool SeederIsIdempotent(PositiveInt n)
    {
        int runCount = (n.Get % 5) + 1; // 1 to 5
        var dbName = $"SeederIdempotent_{Guid.NewGuid()}";
        var (provider, config) = BuildServices(dbName);

        for (int i = 0; i < runCount; i++)
        {
            using var scope = provider.CreateScope();
            RoleSeederService.SeedAsync(scope.ServiceProvider, config)
                .GetAwaiter().GetResult();
        }

        // Verify
        using var verifyScope = provider.CreateScope();
        var roleManager = verifyScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var roles = roleManager.Roles.ToList();
        var admins = userManager.GetUsersInRoleAsync("Admin").GetAwaiter().GetResult();

        provider.Dispose();

        return roles.Count == 6 && admins.Count == 1;
    }
}

// ──────────────────────────────────────────────────────────────────────
// Feature: role-based-authentication, Property 9: Role-based access control enforcement
// ──────────────────────────────────────────────────────────────────────

/// <summary>
/// For any user with a random subset of the 6 roles, FinancialDataAuthorizationService.CanViewFinancialData
/// returns true iff the user has Admin, HR, or Payroll role.
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**
/// </summary>
public class RoleBasedAccessControlPropertyTests
{
    private static readonly string[] AllRoles =
        { "Admin", "HR", "Payroll", "ScheduleManager", "Employee", "DirectManager" };

    private static readonly HashSet<string> FinancialRoles =
        new(new[] { "Admin", "HR", "Payroll" });

    private static Arbitrary<string[]> RoleSubsetArb()
    {
        var gen =
            from flags in Gen.Elements(true, false).ListOf(6)
            let selected = flags.Zip(AllRoles)
                .Where(p => p.First)
                .Select(p => p.Second)
                .ToArray()
            select selected;

        return gen.ToArbitrary();
    }

    private static ClaimsPrincipal CreateUserWithRoles(string[] roles)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "testuser@example.com")
        };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Property(MaxTest = 100)]
    public Property AccessControl_MatchesRolePolicy()
    {
        return Prop.ForAll(RoleSubsetArb(), roles =>
        {
            var service = new FinancialDataAuthorizationService();
            var user = CreateUserWithRoles(roles);

            bool actual = service.CanViewFinancialData(user);
            bool expected = roles.Any(r => FinancialRoles.Contains(r));

            return actual == expected;
        });
    }
}

// ──────────────────────────────────────────────────────────────────────
// Feature: role-based-authentication, Property 10: Financial data visibility by role
// ──────────────────────────────────────────────────────────────────────

/// <summary>
/// For any random subset of the 6 roles, CanViewFinancialData returns true
/// iff the role set intersects {Admin, HR, Payroll}.
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.4**
/// </summary>
public class FinancialDataVisibilityPropertyTests
{
    private static readonly string[] AllRoles =
        { "Admin", "HR", "Payroll", "ScheduleManager", "Employee", "DirectManager" };

    private static readonly HashSet<string> AuthorizedRoles =
        new(new[] { "Admin", "HR", "Payroll" });

    private static Arbitrary<string[]> RoleSubsetArb()
    {
        var gen =
            from flags in Gen.Elements(true, false).ListOf(6)
            let selected = flags.Zip(AllRoles)
                .Where(p => p.First)
                .Select(p => p.Second)
                .ToArray()
            select selected;

        return gen.ToArbitrary();
    }

    private static ClaimsPrincipal CreateUserWithRoles(string[] roles)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "testuser@example.com")
        };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Property(MaxTest = 100)]
    public Property FinancialData_VisibleOnlyToAuthorizedRoles()
    {
        return Prop.ForAll(RoleSubsetArb(), roles =>
        {
            var service = new FinancialDataAuthorizationService();
            var user = CreateUserWithRoles(roles);

            bool canView = service.CanViewFinancialData(user);
            bool shouldView = roles.Any(r => AuthorizedRoles.Contains(r));

            return canView == shouldView;
        });
    }
}

// ──────────────────────────────────────────────────────────────────────
// Feature: role-based-authentication, Property 11: Role assignment round trip
// ──────────────────────────────────────────────────────────────────────

/// <summary>
/// For any role from the 6 roles, creating a user, assigning the role, verifying it exists,
/// then removing the role and verifying it no longer exists — the round trip is consistent.
/// **Validates: Requirements 8.2, 8.3**
/// </summary>
public class RoleAssignmentRoundTripPropertyTests
{
    private static readonly string[] AllRoles =
        { "Admin", "HR", "Payroll", "ScheduleManager", "Employee", "DirectManager" };

    private static ServiceProvider BuildServices(string dbName)
    {
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
        return services.BuildServiceProvider();
    }

    private static Arbitrary<string> RoleArb()
    {
        return Gen.Elements(AllRoles).ToArbitrary();
    }

    [Property(MaxTest = 100)]
    public Property RoleAssignment_RoundTrip_IsConsistent()
    {
        return Prop.ForAll(RoleArb(), role =>
        {
            var dbName = $"RoleRoundTrip_{Guid.NewGuid()}";
            using var provider = BuildServices(dbName);
            using var scope = provider.CreateScope();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Ensure the role exists
            roleManager.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();

            // Create user
            var user = new ApplicationUser
            {
                UserName = $"user_{Guid.NewGuid()}@test.com",
                Email = $"user_{Guid.NewGuid()}@test.com"
            };
            var createResult = userManager.CreateAsync(user, "ValidPass1").GetAwaiter().GetResult();
            if (!createResult.Succeeded) return false;

            // Assign role
            var addResult = userManager.AddToRoleAsync(user, role).GetAwaiter().GetResult();
            if (!addResult.Succeeded) return false;

            // Verify user has the role
            bool hasRoleAfterAdd = userManager.IsInRoleAsync(user, role).GetAwaiter().GetResult();
            if (!hasRoleAfterAdd) return false;

            // Remove role
            var removeResult = userManager.RemoveFromRoleAsync(user, role).GetAwaiter().GetResult();
            if (!removeResult.Succeeded) return false;

            // Verify user no longer has the role
            bool hasRoleAfterRemove = userManager.IsInRoleAsync(user, role).GetAwaiter().GetResult();

            return !hasRoleAfterRemove;
        });
    }
}
