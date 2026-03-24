using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace OrgMgmt.Tests.TestInfrastructure;

/// <summary>
/// Builds WebDriver instances and common browser helpers for the OrgMgmt Selenium suite.
/// </summary>
internal static class SeleniumUiHelpers
{
    private const string DemoModeEnvironmentVariable = "E2E_DEMO_MODE";
    private const string StepDelayEnvironmentVariable = "E2E_STEP_DELAY_MS";
    private const int DefaultStepDelayMilliseconds = 2000;

    /// <summary>
    /// Creates a Chrome driver configured for headless execution and file downloads.
    /// </summary>
    public static IWebDriver CreateDriver(string downloadDirectory)
    {
        var settings = ReadSettings();
        var service = ChromeDriverService.CreateDefaultService();
        service.SuppressInitialDiagnosticInformation = true;
        service.HideCommandPromptWindow = true;

        var options = new ChromeOptions
        {
            BinaryLocation = "/usr/bin/google-chrome"
        };

        if (!settings.DemoMode)
        {
            options.AddArgument("--headless=new");
        }

        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--window-size=1600,1200");
        options.AddUserProfilePreference("download.default_directory", downloadDirectory);
        options.AddUserProfilePreference("download.prompt_for_download", false);
        options.AddUserProfilePreference("download.directory_upgrade", true);
        options.AddUserProfilePreference("plugins.always_open_pdf_externally", true);

        return new ChromeDriver(service, options);
    }

    /// <summary>
    /// Signs in with a seeded user account and waits until the login page is left behind.
    /// </summary>
    public static void Login(IWebDriver driver, Uri rootUri, string email, string password)
    {
        NavigateTo(driver, new Uri(rootUri, "/Account/Login"));
        WaitForText(driver, "Login");

        Type(driver, By.Id("Email"), email);
        Type(driver, By.Id("Password"), password);
        Click(driver, By.CssSelector("button[type='submit']"));

        WaitUntil(driver, browser => !browser.Url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase));
    }

    public static void NavigateTo(IWebDriver driver, Uri uri)
    {
        driver.Navigate().GoToUrl(uri);
        ApplyDemoDelay();
    }

    public static void Click(IWebDriver driver, By by)
    {
        Click(FindVisibleElement(driver, by));
    }

    public static void Click(IWebElement element)
    {
        element.Click();
        ApplyDemoDelay();
    }

    public static void Type(IWebDriver driver, By by, string text)
    {
        Type(FindVisibleElement(driver, by), text);
    }

    public static void Type(IWebElement element, string text)
    {
        element.SendKeys(text);
        ApplyDemoDelay();
    }

    public static void SelectByText(IWebDriver driver, By by, string text)
    {
        SelectByText(FindVisibleElement(driver, by), text);
    }

    public static void SelectByText(IWebElement element, string text)
    {
        new SelectElement(element).SelectByText(text);
        ApplyDemoDelay();
    }

    public static void AcceptAlert(IWebDriver driver)
    {
        new WebDriverWait(driver, TimeSpan.FromSeconds(5)).Until(browser =>
        {
            try
            {
                browser.SwitchTo().Alert().Accept();
                ApplyDemoDelay();
                return true;
            }
            catch (NoAlertPresentException)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Returns the attendance table row for a named employee.
    /// </summary>
    public static IWebElement FindEmployeeRow(IWebDriver driver, string employeeName)
    {
        return FindVisibleElement(driver, By.XPath($"//tbody/tr[td[normalize-space()='{employeeName}']]"));
    }

    /// <summary>
    /// Waits for a displayed element when searching from a driver, or returns the first match from a nested search context.
    /// </summary>
    public static IWebElement FindVisibleElement(ISearchContext searchContext, By by)
    {
        if (searchContext is IWebDriver driver)
        {
            return new WebDriverWait(driver, TimeSpan.FromSeconds(15))
                .Until(browser =>
                {
                    var element = browser.FindElements(by).FirstOrDefault();
                    return element is not null && element.Displayed ? element : null;
                });
        }

        return searchContext.FindElement(by);
    }

    /// <summary>
    /// Waits until the current page source contains the supplied text.
    /// </summary>
    public static void WaitForText(IWebDriver driver, string text)
    {
        WaitUntil(driver, browser => browser.PageSource.Contains(text, StringComparison.Ordinal));
    }

    /// <summary>
    /// Waits until the supplied browser condition becomes true.
    /// </summary>
    public static void WaitUntil(IWebDriver driver, Func<IWebDriver, bool> condition)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
        wait.Until(browser => condition(browser));
    }

    /// <summary>
    /// Creates a temporary download folder for one browser test.
    /// </summary>
    public static string CreateDownloadDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orgmgmt-downloads-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Waits for a downloaded file with the requested extension to appear and finish writing.
    /// </summary>
    public static async Task<string> WaitForDownloadAsync(string downloadDirectory, string extension)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            var matchingFile = Directory.EnumerateFiles(downloadDirectory, $"*{extension}")
                .FirstOrDefault(path => !path.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(matchingFile))
            {
                return matchingFile;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Timed out waiting for a downloaded '{extension}' file.");
    }

    private static SeleniumRunSettings ReadSettings()
    {
        var demoModeValue = Environment.GetEnvironmentVariable(DemoModeEnvironmentVariable);
        var stepDelayValue = Environment.GetEnvironmentVariable(StepDelayEnvironmentVariable);

        var demoMode = bool.TryParse(demoModeValue, out var parsedDemoMode) && parsedDemoMode;
        var stepDelayMilliseconds = int.TryParse(stepDelayValue, out var parsedStepDelayMilliseconds) && parsedStepDelayMilliseconds >= 0
            ? parsedStepDelayMilliseconds
            : DefaultStepDelayMilliseconds;

        return new SeleniumRunSettings(demoMode, TimeSpan.FromMilliseconds(stepDelayMilliseconds));
    }

    private static void ApplyDemoDelay()
    {
        var settings = ReadSettings();
        if (!settings.DemoMode || settings.StepDelay <= TimeSpan.Zero)
        {
            return;
        }

        Thread.Sleep(settings.StepDelay);
    }

    private readonly record struct SeleniumRunSettings(bool DemoMode, TimeSpan StepDelay);
}
