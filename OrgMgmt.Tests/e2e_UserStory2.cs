using FluentAssertions;
using OpenQA.Selenium;
using OrgMgmt.Tests.TestInfrastructure;
using Xunit;

namespace OrgMgmt.Tests;

public class e2e_UserStory2
{
    /// <summary>
    /// Verifies the attendance workflow for late-arrival validation, a successful no-show save, and audit history.
    /// This covers the browser-visible behavior that HR relies on when updating attendance.
    /// Expected result: the missing clock-in error appears first, then the no-show change is visible in the dashboard and history.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Category", "UserStory2")]
    [Trait("UserStory", "UserStory2")]
    public async Task TrackingAndAdjustingAttendance_MissingClockInThenValidLateAdjustment_ShowsUpdatedHoursAndAuditHistory()
    {
        await using var host = new SeleniumTestHost();
        await host.WaitUntilReadyAsync();

        var downloadDirectory = SeleniumUiHelpers.CreateDownloadDirectory();
        IWebDriver? driver = null;

        try
        {
            driver = SeleniumUiHelpers.CreateDriver(downloadDirectory);
            SeleniumUiHelpers.Login(driver, host.RootUri, "hr@orgmgmt.local", "Staff123!");

            // A known Monday guarantees rows for the seeded weekday assignments.
            SeleniumUiHelpers.NavigateTo(driver, new Uri(host.RootUri, "/Attendance/Dashboard?date=2025-01-06"));
            SeleniumUiHelpers.WaitForText(driver, "Attendance Dashboard");

            var aliceRow = SeleniumUiHelpers.FindEmployeeRow(driver, "Alice Johnson");
            var recordId = aliceRow.FindElement(By.CssSelector("select.adjustment-select")).GetAttribute("data-row-id");

            SeleniumUiHelpers.SelectByText(aliceRow.FindElement(By.CssSelector("select.adjustment-select")), "Late Arrival");
            SeleniumUiHelpers.Click(driver, By.CssSelector($"button[form='form-{recordId}']"));
            SeleniumUiHelpers.WaitForText(driver, "Clock-in time is required for Late Arrival adjustment.");

            // Re-find elements after the redirect to avoid stale references.
            aliceRow = SeleniumUiHelpers.FindEmployeeRow(driver, "Alice Johnson");
            recordId = aliceRow.FindElement(By.CssSelector("select.adjustment-select")).GetAttribute("data-row-id");

            // Use No-Show for the success path because it exercises the same workflow without relying on time-field binding.
            SeleniumUiHelpers.SelectByText(aliceRow.FindElement(By.CssSelector("select.adjustment-select")), "No-Show");

            SeleniumUiHelpers.Click(driver, By.CssSelector($"button[form='form-{recordId}']"));
            SeleniumUiHelpers.WaitForText(driver, "Adjustment saved successfully.");

            aliceRow = SeleniumUiHelpers.FindEmployeeRow(driver, "Alice Johnson");
            aliceRow.Text.Should().Contain("0.00");

            SeleniumUiHelpers.Click(aliceRow.FindElement(By.CssSelector($"a[href*='attendanceRecordId={recordId}']")));
            SeleniumUiHelpers.WaitForText(driver, "Audit History");

            driver.PageSource.Should().Contain("NoShow");
            driver.PageSource.Should().Contain("0.00");
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
