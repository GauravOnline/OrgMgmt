using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace OrgMgmt.Tests.TestInfrastructure;

/// <summary>
/// Hosts the MVC application with an in-memory SQLite database for integration-style tests.
/// </summary>
internal sealed class TestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    /// <summary>
    /// Opens the shared in-memory SQLite connection used by the test host.
    /// </summary>
    public TestWebApplicationFactory()
    {
        _connection.Open();
    }

    /// <summary>
    /// Replaces the app database with a test database and injects stable admin settings.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Error);
        });

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminSettings:Email"] = "admin@orgmgmt.local",
                ["AdminSettings:Password"] = "Admin123!",
                ["Logging:LogLevel:Default"] = "Error",
                ["Logging:LogLevel:Microsoft"] = "Error",
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Error"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<OrgDbContext>));
            services.RemoveAll(typeof(DbConnection));

            services.AddSingleton<DbConnection>(_connection);
            services.AddDbContext<OrgDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(serviceProvider.GetRequiredService<DbConnection>());
            });
        });
    }

    /// <summary>
    /// Creates a client that keeps cookies but does not automatically follow redirects.
    /// </summary>
    public HttpClient CreateRedirectlessClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    /// <summary>
    /// Signs in with a seeded user account and returns after the login post completes.
    /// </summary>
    public async Task LoginAsync(HttpClient client, string email, string password, string? returnUrl = null)
    {
        var loginPath = string.IsNullOrWhiteSpace(returnUrl)
            ? "/Account/Login"
            : $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";

        var token = await GetAntiForgeryTokenAsync(client, loginPath);
        var formValues = new Dictionary<string, string?>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = email,
            ["Password"] = password,
            ["ReturnUrl"] = returnUrl ?? string.Empty
        };

        using var response = await client.PostAsync(loginPath, new FormUrlEncodedContent(formValues!));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect, HttpStatusCode.Found);
    }

    /// <summary>
    /// Reads an MVC anti-forgery token from the specified page.
    /// </summary>
    public async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        return ExtractInputValue(html, "__RequestVerificationToken");
    }

    /// <summary>
    /// Posts form values together with the anti-forgery token required by MVC.
    /// </summary>
    public async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, IDictionary<string, string?> values)
    {
        var token = await GetAntiForgeryTokenAsync(client, path);

        // MVC validates the anti-forgery token against the posted form body.
        var formValues = new Dictionary<string, string?>(values)
        {
            ["__RequestVerificationToken"] = token
        };

        return await client.PostAsync(path, new FormUrlEncodedContent(formValues!));
    }

    /// <summary>
    /// Executes a database action inside a fresh DI scope and returns the result.
    /// </summary>
    public async Task<T> ExecuteDbContextAsync<T>(Func<OrgDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrgDbContext>();
        return await action(context);
    }

    /// <summary>
    /// Executes a database action inside a fresh DI scope.
    /// </summary>
    public async Task ExecuteDbContextAsync(Func<OrgDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrgDbContext>();
        await action(context);
    }

    /// <summary>
    /// Disposes the test host and closes the shared in-memory database connection.
    /// </summary>
    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// Extracts the value of a named input from an HTML response.
    /// </summary>
    private static string ExtractInputValue(string html, string inputName)
    {
        var pattern = $@"<input[^>]*(?:name=""{Regex.Escape(inputName)}""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""{Regex.Escape(inputName)}"")[^>]*>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find input '{inputName}' in the HTML response.");
        }

        var rawValue = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return WebUtility.HtmlDecode(rawValue);
    }
}
