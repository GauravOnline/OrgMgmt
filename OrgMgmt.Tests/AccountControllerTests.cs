using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrgMgmt.Controllers;
using OrgMgmt.Models;
using OrgMgmt.ViewModels;
using Xunit;

namespace OrgMgmt.Tests;

public class AccountControllerTests
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

        services.AddAuthentication();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        // Add MVC services needed by Controller base class
        services.AddControllersWithViews();

        return services.BuildServiceProvider();
    }

    private static AccountController CreateController(
        IServiceScope scope,
        DefaultHttpContext httpContext)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var controller = new AccountController(userManager, signInManager);

        // Set HttpContext on the accessor so SignInManager can find it
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = httpContext;

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        // Set up Url helper
        controller.Url = new UrlHelper(new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor()));

        return controller;
    }

    private static DefaultHttpContext CreateHttpContext(IServiceScope scope)
    {
        return new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
    }

    [Fact]
    public async Task Register_ValidData_CreatesUserAndRedirects()
    {
        var provider = BuildServices(nameof(Register_ValidData_CreatesUserAndRedirects));
        using var scope = provider.CreateScope();
        var httpContext = CreateHttpContext(scope);
        var controller = CreateController(scope, httpContext);

        var model = new RegisterViewModel
        {
            Email = "test@example.com",
            Password = "Test123",
            ConfirmPassword = "Test123"
        };

        var result = await controller.Register(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);

        // Verify user was created
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("test@example.com");
        Assert.NotNull(user);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ShowsError()
    {
        var provider = BuildServices(nameof(Register_DuplicateEmail_ShowsError));
        using var scope = provider.CreateScope();
        var httpContext = CreateHttpContext(scope);

        // Create first user directly
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existingUser = new ApplicationUser { UserName = "dup@example.com", Email = "dup@example.com" };
        await userManager.CreateAsync(existingUser, "Test123");

        var controller = CreateController(scope, httpContext);
        var model = new RegisterViewModel
        {
            Email = "dup@example.com",
            Password = "Test123",
            ConfirmPassword = "Test123"
        };

        var result = await controller.Register(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Login_ValidCredentials_RedirectsToHome()
    {
        var provider = BuildServices(nameof(Login_ValidCredentials_RedirectsToHome));
        using var scope = provider.CreateScope();
        var httpContext = CreateHttpContext(scope);

        // Create user first
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "login@example.com", Email = "login@example.com" };
        await userManager.CreateAsync(user, "Test123");

        var controller = CreateController(scope, httpContext);
        var model = new LoginViewModel
        {
            Email = "login@example.com",
            Password = "Test123"
        };

        var result = await controller.Login(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }

    [Fact]
    public async Task Logout_RedirectsToHome()
    {
        var provider = BuildServices(nameof(Logout_RedirectsToHome));
        using var scope = provider.CreateScope();
        var httpContext = CreateHttpContext(scope);
        var controller = CreateController(scope, httpContext);

        var result = await controller.Logout();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }
}
