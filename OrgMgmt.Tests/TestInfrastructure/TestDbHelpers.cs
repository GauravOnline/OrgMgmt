using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;

namespace OrgMgmt.Tests.TestInfrastructure;

/// <summary>
/// Creates common in-memory entities and EF Core contexts for unit-style tests.
/// </summary>
internal static class TestDbHelpers
{
    /// <summary>
    /// Creates an isolated in-memory <see cref="OrgDbContext"/> for a single test.
    /// </summary>
    public static OrgDbContext CreateInMemoryContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<OrgDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new OrgDbContext(options);
    }

    /// <summary>
    /// Builds an employee with sensible defaults for tests that only care about a few fields.
    /// </summary>
    public static Employee CreateEmployee(
        string name = "Alice Johnson",
        string role = "Nurse",
        bool isActive = true,
        decimal hourlyPayRate = 32.50m)
    {
        return new Employee
        {
            ID = Guid.NewGuid(),
            Name = name,
            Address = "Burnaby",
            DateOfBirth = new DateTime(1990, 1, 1),
            Role = role,
            IsActive = isActive,
            HourlyPayRate = hourlyPayRate
        };
    }

    /// <summary>
    /// Builds a shift with the default recurrence and time window used by most tests.
    /// </summary>
    public static Shift CreateShift(
        string name = "Morning Shift",
        string location = "Ward A",
        TimeSpan? startTime = null,
        TimeSpan? endTime = null,
        int interval = 1,
        string daysOfWeek = "Monday")
    {
        return new Shift
        {
            Id = Guid.NewGuid(),
            Name = name,
            Location = location,
            StartTime = startTime ?? new TimeSpan(7, 0, 0),
            EndTime = endTime ?? new TimeSpan(15, 0, 0),
            Interval = interval,
            DaysOfWeek = daysOfWeek,
            Frequency = Frequency.Weekly
        };
    }

    /// <summary>
    /// Creates an attendance record already linked to the supplied employee and shift.
    /// </summary>
    public static AttendanceRecord CreateAttendanceRecord(
        Employee employee,
        Shift shift,
        DateTime targetDate,
        AdjustmentType adjustmentType = AdjustmentType.None,
        decimal hoursToPay = 8.00m)
    {
        return new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.ID,
            Employee = employee,
            ShiftId = shift.Id,
            Shift = shift,
            TargetDate = targetDate.Date,
            AdjustmentType = adjustmentType,
            HoursToPay = hoursToPay
        };
    }
}

/// <summary>
/// Attaches the MVC context pieces needed by controller tests that use TempData or HttpContext.
/// </summary>
internal static class ControllerTestHelpers
{
    /// <summary>
    /// Creates a basic controller context and in-memory TempData store for the supplied controller.
    /// </summary>
    public static void AttachControllerContext(Controller controller)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            new InMemoryTempDataProvider());
    }
}

/// <summary>
/// Stores TempData values in memory for controller tests.
/// </summary>
internal sealed class InMemoryTempDataProvider : ITempDataProvider
{
    private Dictionary<string, object?> _values = new();

    /// <summary>
    /// Returns the current TempData snapshot for the active test request.
    /// </summary>
    public IDictionary<string, object?> LoadTempData(HttpContext context)
    {
        return new Dictionary<string, object?>(_values);
    }

    /// <summary>
    /// Saves TempData values so later controller reads can retrieve them.
    /// </summary>
    public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
    {
        _values = values.ToDictionary(pair => pair.Key, pair => pair.Value);
    }
}
