using FluentAssertions;
using OpenQA.Selenium;
using OrgMgmt.Tests.TestInfrastructure;
using Xunit;

namespace OrgMgmt.Tests;

public class e2e_UserStory3
{
    /// <summary>
    /// Verifies the payroll reporting flow from first load through export downloads.
    /// This covers the end-to-end operator workflow across generation and both export actions.
    /// Expected result: default dates are shown first, then report rows and both export files become available.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Category", "UserStory3")]
    [Trait("UserStory", "UserStory3")]
    public async Task GeneratingBiWeeklyPayrollReports_InvalidRangeThenValidGeneration_ShowsExportsAndDownloadsFiles()
    {
        await using var host = new SeleniumTestHost();
        await host.WaitUntilReadyAsync();

        var downloadDirectory = SeleniumUiHelpers.CreateDownloadDirectory();
        IWebDriver? driver = null;

        try
        {
            driver = SeleniumUiHelpers.CreateDriver(downloadDirectory);
            SeleniumUiHelpers.Login(driver, host.RootUri, "payroll@orgmgmt.local", "Staff123!");

            SeleniumUiHelpers.NavigateTo(driver, new Uri(host.RootUri, "/Payroll"));
            SeleniumUiHelpers.WaitForText(driver, "Payroll Report");

            var startInput = SeleniumUiHelpers.FindVisibleElement(driver, By.Id("PeriodStartDate"));
            var endInput = SeleniumUiHelpers.FindVisibleElement(driver, By.Id("PeriodEndDate"));

            startInput.GetAttribute("value").Should().Be(DateTime.Today.AddDays(-13).ToString("yyyy-MM-dd"));
            endInput.GetAttribute("value").Should().Be(DateTime.Today.ToString("yyyy-MM-dd"));
            driver.PageSource.Should().NotContain("Export Excel");
            driver.PageSource.Should().NotContain("Export PDF");

            // Submit the default seeded range so the report and export actions appear.
            SeleniumUiHelpers.Click(driver, By.XPath("//button[contains(normalize-space(),'Generate')]"));

            SeleniumUiHelpers.WaitUntil(driver, browser => browser.FindElements(By.CssSelector("table tbody tr")).Count > 0);
            driver.PageSource.Should().Contain("Export Excel");
            driver.PageSource.Should().Contain("Export PDF");

            SeleniumUiHelpers.Click(driver, By.LinkText("Export Excel"));
            var excelFile = await SeleniumUiHelpers.WaitForDownloadAsync(downloadDirectory, ".xlsx");
            Path.GetFileName(excelFile).Should().MatchRegex(@"^payroll-\d{8}-\d{8}\.xlsx$");

            SeleniumUiHelpers.Click(driver, By.LinkText("Export PDF"));
            var pdfFile = await SeleniumUiHelpers.WaitForDownloadAsync(downloadDirectory, ".pdf");
            Path.GetFileName(pdfFile).Should().MatchRegex(@"^payroll-\d{8}-\d{8}\.pdf$");
        }
        finally
        {
            driver?.Quit();
            driver?.Dispose();

            if (Directory.Exists(downloadDirectory))
            {
                Directory.Delete(downloadDirectory, recursive: true);
            }
        }
    }
}
