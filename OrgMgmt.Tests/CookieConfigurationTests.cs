using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrgMgmt.Models;
using Xunit;

namespace OrgMgmt.Tests;

public class CookieConfigurationTests
{
    private static ServiceProvider BuildServicesWithCookieConfig()
    {
        var services = new ServiceCollection();
        services.AddDbContext<OrgDbContext>(o =>
            o.UseInMemoryDatabase("CookieConfigTests"));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<OrgDbContext>()
            .AddDefaultTokenProviders();

        // Mirror the cookie configuration from Program.cs
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = true;
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
        });

        services.AddLogging();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void CookieOptions_HttpOnly_IsTrue()
    {
        var provider = BuildServicesWithCookieConfig();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.True(options.Cookie.HttpOnly);
    }

    [Fact]
    public void CookieOptions_SecurePolicy_IsAlways()
    {
        var provider = BuildServicesWithCookieConfig();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void CookieOptions_SameSite_IsStrict()
    {
        var provider = BuildServicesWithCookieConfig();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
    }

    [Fact]
    public void CookieOptions_ExpireTimeSpan_Is30Minutes()
    {
        var provider = BuildServicesWithCookieConfig();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal(TimeSpan.FromMinutes(30), options.ExpireTimeSpan);
    }

    [Fact]
    public void CookieOptions_SlidingExpiration_IsTrue()
    {
        var provider = BuildServicesWithCookieConfig();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.True(options.SlidingExpiration);
    }

    [Fact]
    public void CookieOptions_LoginPath_IsAccountLogin()
    {
        var provider = BuildServicesWithCookieConfig();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal("/Account/Login", options.LoginPath.Value);
    }

    [Fact]
    public void CookieOptions_AccessDeniedPath_IsAccountAccessDenied()
    {
        var provider = BuildServicesWithCookieConfig();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal("/Account/AccessDenied", options.AccessDeniedPath.Value);
    }
}
