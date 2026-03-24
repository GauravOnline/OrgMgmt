using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrgMgmt.Controllers;
using OrgMgmt.Models;
using OrgMgmt.Services;
using OrgMgmt.Tests.TestInfrastructure;
using OrgMgmt.ViewModels;
using Xunit;

namespace OrgMgmt.Tests;

public class AttendanceControllerTests
{
    /// <summary>
    /// Verifies that the dashboard action returns a populated model for a valid date.
    /// This protects the main HR page that starts the attendance workflow.
    /// Expected result: the view model contains the scheduled employee row.
    /// </summary>
    [Fact]
    public async Task Dashboard_ValidDate_ReturnsPopulatedModel()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(Dashboard_ValidDate_ReturnsPopulatedModel));
        var selectedDate = new DateTime(2025, 1, 6);
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift(daysOfWeek: "Monday");

        employee.Shifts.Add(shift);

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var controller = new AttendanceController(new AttendanceService(context));
        ControllerTestHelpers.AttachControllerContext(controller);

        var result = await controller.Dashboard(selectedDate);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<AttendanceDashboardViewModel>().Subject;
        model.SelectedDate.Should().Be(selectedDate);
        model.Rows.Should().ContainSingle(row => row.EmployeeName == employee.Name);
    }

    /// <summary>
    /// Confirms that a valid adjustment redirects back with a success message.
    /// This preserves the user feedback shown after a successful attendance update.
    /// Expected result: TempData contains the success message for the dashboard.
    /// </summary>
    [Fact]
    public async Task SaveAdjustment_ValidInput_RedirectsBackWithSuccessTempData()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(SaveAdjustment_ValidInput_RedirectsBackWithSuccessTempData));
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift();
        var record = TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 6));

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        context.AttendanceRecords.Add(record);
        await context.SaveChangesAsync();

        var controller = new AttendanceController(new AttendanceService(context));
        ControllerTestHelpers.AttachControllerContext(controller);

        // Supply a user id because the service records it in the audit log.
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "hr-user")
        ], "TestAuth"));

        var model = new AdjustmentFormViewModel
        {
            AttendanceRecordId = record.Id,
            AdjustmentType = AdjustmentType.Late,
            ClockInTime = new DateTime(2025, 1, 6, 7, 30, 0),
            Notes = "Traffic delay",
            SelectedDate = new DateTime(2025, 1, 6)
        };

        var result = await controller.SaveAdjustment(model);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(AttendanceController.Dashboard));
        redirect.RouteValues!["date"].Should().Be("2025-01-06");
        controller.TempData["Success"].Should().Be("Adjustment saved successfully.");
    }

    /// <summary>
    /// Ensures that an invalid adjustment post redirects back with an error message.
    /// This keeps bad form submissions from failing silently.
    /// Expected result: TempData contains the invalid submission error.
    /// </summary>
    [Fact]
    public async Task SaveAdjustment_InvalidInput_RedirectsBackWithErrorTempData()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(SaveAdjustment_InvalidInput_RedirectsBackWithErrorTempData));
        var controller = new AttendanceController(new AttendanceService(context));
        ControllerTestHelpers.AttachControllerContext(controller);

        // Seed model-state failure to mimic MVC rejecting the posted form.
        controller.ModelState.AddModelError("AttendanceRecordId", "Attendance record is required.");

        var model = new AdjustmentFormViewModel
        {
            AttendanceRecordId = Guid.NewGuid(),
            AdjustmentType = AdjustmentType.None,
            SelectedDate = new DateTime(2025, 1, 6)
        };

        var result = await controller.SaveAdjustment(model);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(AttendanceController.Dashboard));
        redirect.RouteValues!["date"].Should().Be("2025-01-06");
        controller.TempData["Error"].Should().Be("Invalid form submission.");
    }

    /// <summary>
    /// Verifies that audit history returns the projected entries for a record.
    /// This supports the screen staff use to review attendance changes.
    /// Expected result: the model contains the ordered history items.
    /// </summary>
    [Fact]
    public async Task AuditHistory_ExistingEntries_ReturnsExpectedHistoryModel()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(AuditHistory_ExistingEntries_ReturnsExpectedHistoryModel));
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift();
        var record = TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 6));

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        context.AttendanceRecords.Add(record);
        context.AuditLogEntries.AddRange(
            new AuditLogEntry
            {
                AttendanceRecordId = record.Id,
                UserId = "hr-one",
                Timestamp = new DateTime(2025, 1, 6, 9, 0, 0, DateTimeKind.Utc),
                PreviousAdjustmentType = AdjustmentType.None,
                NewAdjustmentType = AdjustmentType.Sick,
                PreviousHoursToPay = 8.00m,
                NewHoursToPay = 8.00m
            },
            new AuditLogEntry
            {
                AttendanceRecordId = record.Id,
                UserId = "hr-two",
                Timestamp = new DateTime(2025, 1, 6, 10, 0, 0, DateTimeKind.Utc),
                PreviousAdjustmentType = AdjustmentType.Sick,
                NewAdjustmentType = AdjustmentType.Late,
                PreviousHoursToPay = 8.00m,
                NewHoursToPay = 7.50m
            });
        await context.SaveChangesAsync();

        var controller = new AttendanceController(new AttendanceService(context));
        ControllerTestHelpers.AttachControllerContext(controller);

        var result = await controller.AuditHistory(record.Id);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeAssignableTo<List<AuditLogEntryViewModel>>().Subject;
        model.Should().HaveCount(2);
        model.Select(entry => entry.UserName).Should().ContainInOrder("hr-one", "hr-two");
        model[1].NewAdjustmentType.Should().Be(AdjustmentType.Late);
    }

    /// <summary>
    /// Confirms that attendance routes deny anonymous users and users in the wrong role.
    /// This protects attendance data behind the intended role checks.
    /// Expected result: anonymous users go to login and employees go to access denied.
    /// </summary>
    [Fact]
    public async Task AttendanceRoutes_UnauthorizedUsers_AreDeniedAccess()
    {
        await using var factory = new TestWebApplicationFactory();

        using var anonymousClient = factory.CreateRedirectlessClient();
        using var anonymousResponse = await anonymousClient.GetAsync("/Attendance/Dashboard");

        anonymousResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        anonymousResponse.Headers.Location.Should().NotBeNull();
        anonymousResponse.Headers.Location!.PathAndQuery.Should().StartWith("/Account/Login");

        using var employeeClient = factory.CreateRedirectlessClient();
        await factory.LoginAsync(employeeClient, "employee@orgmgmt.local", "Staff123!");

        using var employeeDashboardResponse = await employeeClient.GetAsync("/Attendance/Dashboard");
        using var employeeAuditResponse = await employeeClient.GetAsync($"/Attendance/AuditHistory/{Guid.NewGuid()}");

        employeeDashboardResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        employeeDashboardResponse.Headers.Location!.PathAndQuery.Should().StartWith("/Account/AccessDenied");
        employeeAuditResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        employeeAuditResponse.Headers.Location!.PathAndQuery.Should().StartWith("/Account/AccessDenied");
    }
}
