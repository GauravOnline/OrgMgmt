using FluentAssertions;
using OpenQA.Selenium;
using OrgMgmt.Tests.TestInfrastructure;
using Xunit;

namespace OrgMgmt.Tests;

public class e2e_UserStory1
{
    /// <summary>
    /// Verifies the scheduler flow for assigning and removing a valid bi-weekly shift.
    /// This covers the real browser path for schedule management after redirects and form posts.
    /// Expected result: the shift appears in current assignments and is removed cleanly.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Category", "UserStory1")]
    [Trait("UserStory", "UserStory1")]
    public async Task AssigningEmployeeSchedules_ValidBiWeeklyAssignmentAndRemoval_UpdatesCurrentAssignments()
    {
        await using var host = new SeleniumTestHost();
        await host.WaitUntilReadyAsync();

        var downloadDirectory = SeleniumUiHelpers.CreateDownloadDirectory();
        IWebDriver? driver = null;

        try
        {
            driver = SeleniumUiHelpers.CreateDriver(downloadDirectory);
            SeleniumUiHelpers.Login(driver, host.RootUri, "scheduler@orgmgmt.local", "Staff123!");

            SeleniumUiHelpers.NavigateTo(driver, new Uri(host.RootUri, "/Schedule/Assign"));
            SeleniumUiHelpers.WaitForText(driver, "Schedule Assignment");

            // The seeded inactive employee should not appear on the assignment list.
            driver.PageSource.Should().Contain("Alice Johnson");
            driver.PageSource.Should().NotContain("Frank Wilson");

            SeleniumUiHelpers.Click(
                SeleniumUiHelpers.FindVisibleElement(
                    driver,
                    By.XPath("//tr[td[normalize-space()='Alice Johnson']]//a[contains(normalize-space(),'Manage Shifts')]")));
            SeleniumUiHelpers.WaitForText(driver, "Alice Johnson");

            // This seeded shift should not overlap with Alice's existing weekday assignment.
            SeleniumUiHelpers.SelectByText(
                driver,
                By.Id("shiftId"),
                "Weekend Day — Ward A | 08:00–16:00 | Saturday,Sunday | Bi-weekly");

            SeleniumUiHelpers.Click(driver, By.XPath("//button[contains(normalize-space(),'Assign Shift')]"));
            SeleniumUiHelpers.WaitForText(driver, "Successfully assigned");

            // Read only the current-assignment section so the check is not confused by the available-shifts list.
            var currentAssignments = SeleniumUiHelpers.FindVisibleElement(driver, By.CssSelector("section[aria-label='Current shift assignments']"));
            currentAssignments.Text.Should().Contain("Weekend Day");
            currentAssignments.Text.Should().Contain("Bi-weekly");

            SeleniumUiHelpers.Click(
                SeleniumUiHelpers.FindVisibleElement(
                    driver,
                    By.XPath("//section[@aria-label='Current shift assignments']//tr[td[normalize-space()='Weekend Day']]//button[contains(normalize-space(),'Remove')]")));

            // The remove flow uses a browser confirm dialog.
            SeleniumUiHelpers.AcceptAlert(driver);

            SeleniumUiHelpers.WaitUntil(driver, browser =>
            {
                var text = SeleniumUiHelpers.FindVisibleElement(browser, By.CssSelector("section[aria-label='Current shift assignments']")).Text;
                return !text.Contains("Weekend Day", StringComparison.Ordinal);
            });
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
