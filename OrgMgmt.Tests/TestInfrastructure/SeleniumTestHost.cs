using System.Net.Sockets;
using System.Diagnostics;

namespace OrgMgmt.Tests.TestInfrastructure;

/// <summary>
/// Starts the OrgMgmt application on a temporary local URL for Selenium end-to-end tests.
/// </summary>
internal sealed class SeleniumTestHost : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _databaseFilePath;

    /// <summary>
    /// Creates a temporary database file and starts the application process for the test run.
    /// </summary>
    public SeleniumTestHost()
    {
        ProjectDirectory = FindProjectDirectory();
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"orgmgmt-selenium-{Guid.NewGuid():N}.db");
        Port = GetFreePort();
        RootUri = new Uri($"http://127.0.0.1:{Port}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --project \"{Path.Combine(ProjectDirectory, "OrgMgmt.csproj")}\" --urls {RootUri}",
            WorkingDirectory = ProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={_databaseFilePath}";
        startInfo.Environment["AdminSettings__Email"] = "admin@orgmgmt.local";
        startInfo.Environment["AdminSettings__Password"] = "Admin123!";
        startInfo.Environment["Logging__LogLevel__Default"] = "Error";
        startInfo.Environment["Logging__LogLevel__Microsoft"] = "Error";
        startInfo.Environment["Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command"] = "Error";

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the OrgMgmt application for Selenium tests.");
    }

    /// <summary>
    /// Returns the application project directory used to launch the test host.
    /// </summary>
    public string ProjectDirectory { get; }

    /// <summary>
    /// Returns the dynamically assigned local port used by the test host.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Returns the root URL of the running Selenium test host.
    /// </summary>
    public Uri RootUri { get; }

    /// <summary>
    /// Waits until the application responds to HTTP requests or fails fast if startup crashes.
    /// </summary>
    public async Task WaitUntilReadyAsync()
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                var output = await _process.StandardOutput.ReadToEndAsync();
                var error = await _process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException(
                    $"The OrgMgmt application exited before Selenium could connect.{Environment.NewLine}STDOUT:{Environment.NewLine}{output}{Environment.NewLine}STDERR:{Environment.NewLine}{error}");
            }

            try
            {
                using var response = await httpClient.GetAsync(new Uri(RootUri, "/Account/Login"));
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // The app may still be starting up, so keep polling until the deadline.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("Timed out waiting for the OrgMgmt application to start for Selenium.");
    }

    /// <summary>
    /// Stops the application process and removes the temporary Selenium database file.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();

        if (File.Exists(_databaseFilePath))
        {
            File.Delete(_databaseFilePath);
        }
    }

    /// <summary>
    /// Walks up from the test output directory until it finds the OrgMgmt project file.
    /// </summary>
    private static string FindProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var projectPath = Path.Combine(directory.FullName, "OrgMgmt.csproj");
            if (File.Exists(projectPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the OrgMgmt project directory from the test output path.");
    }

    /// <summary>
    /// Reserves an available local TCP port for the temporary application host.
    /// </summary>
    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
