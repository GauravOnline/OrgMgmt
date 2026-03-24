using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;
using OrgMgmt.Services;
using OrgMgmt.Tests.TestInfrastructure;
using Xunit;

namespace OrgMgmt.Tests;

public class AttendanceServiceTests
{
    /// <summary>
    /// Verifies that the default attendance state keeps the full scheduled hours.
    /// This preserves the baseline used by later attendance and payroll calculations.
    /// Expected result: an eight-hour shift returns 8.00 payable hours.
    /// </summary>
    [Fact]
    public void CalculateHoursToPay_NoneAdjustment_ReturnsFullShiftHours()
    {
        var hoursToPay = AttendanceService.CalculateHoursToPay(
            AdjustmentType.None,
            new TimeSpan(7, 0, 0),
            new TimeSpan(15, 0, 0),
            null);

        hoursToPay.Should().Be(8.00m);
    }

    /// <summary>
    /// Confirms that sick leave stays fully paid in the current attendance rules.
    /// This keeps paid leave from being undercounted in payroll.
    /// Expected result: an eight-hour shift returns 8.00 payable hours.
    /// </summary>
    [Fact]
    public void CalculateHoursToPay_SickAdjustment_ReturnsFullShiftHours()
    {
        var hoursToPay = AttendanceService.CalculateHoursToPay(
            AdjustmentType.Sick,
            new TimeSpan(7, 0, 0),
            new TimeSpan(15, 0, 0),
            null);

        hoursToPay.Should().Be(8.00m);
    }

    /// <summary>
    /// Confirms that vacation time keeps the full scheduled pay.
    /// This keeps approved leave aligned with the payroll rules in the service.
    /// Expected result: an eight-hour shift returns 8.00 payable hours.
    /// </summary>
    [Fact]
    public void CalculateHoursToPay_VacationAdjustment_ReturnsFullShiftHours()
    {
        var hoursToPay = AttendanceService.CalculateHoursToPay(
            AdjustmentType.Vacation,
            new TimeSpan(7, 0, 0),
            new TimeSpan(15, 0, 0),
            null);

        hoursToPay.Should().Be(8.00m);
    }

    /// <summary>
    /// Verifies that a no-show never contributes paid hours.
    /// This prevents missed shifts from leaking into payroll totals.
    /// Expected result: the calculation returns 0.00.
    /// </summary>
    [Fact]
    public void CalculateHoursToPay_NoShowAdjustment_ReturnsZero()
    {
        var hoursToPay = AttendanceService.CalculateHoursToPay(
            AdjustmentType.NoShow,
            new TimeSpan(7, 0, 0),
            new TimeSpan(15, 0, 0),
            null);

        hoursToPay.Should().Be(0.00m);
    }

    /// <summary>
    /// Verifies that a late arrival reduces pay from the actual clock-in time.
    /// This protects the main adjustment rule used for late attendance.
    /// Expected result: a 7:30 arrival on a 7:00 to 15:00 shift returns 7.50 hours.
    /// </summary>
    [Fact]
    public void CalculateHoursToPay_LateAdjustment_ReducesHoursCorrectly()
    {
        var hoursToPay = AttendanceService.CalculateHoursToPay(
            AdjustmentType.Late,
            new TimeSpan(7, 0, 0),
            new TimeSpan(15, 0, 0),
            new DateTime(2025, 1, 6, 7, 30, 0));

        hoursToPay.Should().Be(7.50m);
    }

    /// <summary>
    /// Confirms that late adjustments require a clock-in time.
    /// This avoids calculating reduced hours from incomplete data.
    /// Expected result: the service rejects the request with a validation error.
    /// </summary>
    [Fact]
    public async Task ApplyAdjustmentAsync_LateAdjustmentWithoutClockIn_IsRejected()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(ApplyAdjustmentAsync_LateAdjustmentWithoutClockIn_IsRejected));
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift();
        var record = TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 6));

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        context.AttendanceRecords.Add(record);
        await context.SaveChangesAsync();

        var service = new AttendanceService(context);

        var result = await service.ApplyAdjustmentAsync(record.Id, AdjustmentType.Late, null, "Missing clock-in", "hr-user");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Clock-in time is required for Late Arrival adjustment.");
    }

    /// <summary>
    /// Ensures that late clock-in times stay inside the scheduled shift window.
    /// This blocks invalid adjustments that would distort worked-time calculations.
    /// Expected result: the service rejects times outside the shift bounds.
    /// </summary>
    [Fact]
    public async Task ApplyAdjustmentAsync_LateClockInOutsideShiftWindow_IsRejected()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(ApplyAdjustmentAsync_LateClockInOutsideShiftWindow_IsRejected));
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift(startTime: new TimeSpan(9, 0, 0), endTime: new TimeSpan(17, 0, 0));
        var record = TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 6));

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        context.AttendanceRecords.Add(record);
        await context.SaveChangesAsync();

        var service = new AttendanceService(context);

        var result = await service.ApplyAdjustmentAsync(
            record.Id,
            AdjustmentType.Late,
            new DateTime(2025, 1, 6, 18, 0, 0),
            "Outside shift",
            "hr-user");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Clock-in time must be within the shift start and end times.");
    }

    /// <summary>
    /// Verifies that a no-show clears previously saved attendance times.
    /// This protects payroll from treating a missed shift like worked time.
    /// Expected result: the saved record has null clock times and 0 payable hours.
    /// </summary>
    [Fact]
    public async Task ApplyAdjustmentAsync_NoShowAdjustment_ClearsClockInAndClockOut()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(ApplyAdjustmentAsync_NoShowAdjustment_ClearsClockInAndClockOut));
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift();
        var record = TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 6));

        // Start with saved times so the test proves the service clears them.
        record.ClockInTime = new DateTime(2025, 1, 6, 7, 0, 0);
        record.ClockOutTime = new DateTime(2025, 1, 6, 15, 0, 0);

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        context.AttendanceRecords.Add(record);
        await context.SaveChangesAsync();

        var service = new AttendanceService(context);

        var result = await service.ApplyAdjustmentAsync(record.Id, AdjustmentType.NoShow, null, "Did not arrive", "hr-user");

        result.Success.Should().BeTrue();

        var savedRecord = await context.AttendanceRecords.SingleAsync();
        savedRecord.AdjustmentType.Should().Be(AdjustmentType.NoShow);
        savedRecord.HoursToPay.Should().Be(0.00m);
        savedRecord.ClockInTime.Should().BeNull();
        savedRecord.ClockOutTime.Should().BeNull();
    }

    /// <summary>
    /// Confirms that saving an adjustment also writes an audit log entry.
    /// This keeps attendance changes traceable after staff edits.
    /// Expected result: the audit row captures the old and new adjustment values.
    /// </summary>
    [Fact]
    public async Task ApplyAdjustmentAsync_ValidAdjustment_CreatesAuditEntryWithOldAndNewValues()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(ApplyAdjustmentAsync_ValidAdjustment_CreatesAuditEntryWithOldAndNewValues));
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift();
        var record = TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 6), AdjustmentType.None, 8.00m);

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        context.AttendanceRecords.Add(record);
        await context.SaveChangesAsync();

        var service = new AttendanceService(context);

        var result = await service.ApplyAdjustmentAsync(
            record.Id,
            AdjustmentType.Late,
            new DateTime(2025, 1, 6, 7, 30, 0),
            "Traffic delay",
            "hr-user");

        result.Success.Should().BeTrue();

        var auditEntry = await context.AuditLogEntries.SingleAsync();
        auditEntry.AttendanceRecordId.Should().Be(record.Id);
        auditEntry.UserId.Should().Be("hr-user");
        auditEntry.PreviousAdjustmentType.Should().Be(AdjustmentType.None);
        auditEntry.NewAdjustmentType.Should().Be(AdjustmentType.Late);
        auditEntry.PreviousHoursToPay.Should().Be(8.00m);
        auditEntry.NewHoursToPay.Should().Be(7.50m);
    }

    /// <summary>
    /// Verifies that audit history is returned in timestamp order.
    /// This keeps the change timeline readable in the order events happened.
    /// Expected result: earlier changes appear before later ones.
    /// </summary>
    [Fact]
    public async Task GetAuditHistoryAsync_MultipleEntries_ReturnsEntriesInTimestampOrder()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(GetAuditHistoryAsync_MultipleEntries_ReturnsEntriesInTimestampOrder));
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
                UserId = "hr-two",
                Timestamp = new DateTime(2025, 1, 6, 10, 0, 0, DateTimeKind.Utc),
                PreviousAdjustmentType = AdjustmentType.Sick,
                NewAdjustmentType = AdjustmentType.None,
                PreviousHoursToPay = 8.00m,
                NewHoursToPay = 8.00m
            },
            new AuditLogEntry
            {
                AttendanceRecordId = record.Id,
                UserId = "hr-one",
                Timestamp = new DateTime(2025, 1, 6, 9, 0, 0, DateTimeKind.Utc),
                PreviousAdjustmentType = AdjustmentType.None,
                NewAdjustmentType = AdjustmentType.Sick,
                PreviousHoursToPay = 8.00m,
                NewHoursToPay = 8.00m
            });
        await context.SaveChangesAsync();

        var service = new AttendanceService(context);

        var history = await service.GetAuditHistoryAsync(record.Id);

        history.Should().HaveCount(2);
        history.Select(entry => entry.UserName).Should().ContainInOrder("hr-one", "hr-two");
        history.Select(entry => entry.Timestamp).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// Ensures that the dashboard creates missing attendance rows for scheduled active employees.
    /// This lets HR work from the dashboard without creating daily records by hand.
    /// Expected result: the view model and database both contain the new row.
    /// </summary>
    [Fact]
    public async Task GetDashboardAsync_ScheduledActiveEmployeeWithoutRecord_CreatesAttendanceRow()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(GetDashboardAsync_ScheduledActiveEmployeeWithoutRecord_CreatesAttendanceRow));
        var selectedDate = new DateTime(2025, 1, 6);
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift(daysOfWeek: "Monday");

        // Wire the many-to-many assignment so the dashboard treats the employee as scheduled.
        employee.Shifts.Add(shift);

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var service = new AttendanceService(context);

        var model = await service.GetDashboardAsync(selectedDate);

        model.Rows.Should().HaveCount(1);
        model.Rows[0].EmployeeName.Should().Be(employee.Name);
        model.Rows[0].HoursToPay.Should().Be(8.00m);

        var savedRecord = await context.AttendanceRecords.SingleAsync();
        savedRecord.EmployeeId.Should().Be(employee.ID);
        savedRecord.TargetDate.Should().Be(selectedDate.Date);
        savedRecord.AdjustmentType.Should().Be(AdjustmentType.None);
    }

    /// <summary>
    /// Confirms that inactive employees are skipped even if they still have assigned shifts.
    /// This keeps attendance and payroll screens focused on active staff only.
    /// Expected result: no dashboard rows or attendance records are created.
    /// </summary>
    [Fact]
    public async Task GetDashboardAsync_InactiveEmployeeWithShift_DoesNotCreateAttendanceRow()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(GetDashboardAsync_InactiveEmployeeWithShift_DoesNotCreateAttendanceRow));
        var selectedDate = new DateTime(2025, 1, 6);
        var employee = TestDbHelpers.CreateEmployee(name: "Frank Wilson", isActive: false);
        var shift = TestDbHelpers.CreateShift(daysOfWeek: "Monday");

        employee.Shifts.Add(shift);

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var service = new AttendanceService(context);

        var model = await service.GetDashboardAsync(selectedDate);

        model.Rows.Should().BeEmpty();
        model.ErrorMessage.Should().Be("No scheduled employees for this date.");
        context.AttendanceRecords.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the weekly and bi-weekly recurrence rules used by attendance scheduling.
    /// This drives which employees appear on the dashboard for a given date.
    /// Expected result: weekly shifts match each listed day, while bi-weekly shifts skip inactive weeks.
    /// </summary>
    [Fact]
    public void IsShiftScheduledOnDate_WeeklyAndBiWeeklyIntervals_ReturnExpectedMatches()
    {
        var weeklyShift = TestDbHelpers.CreateShift(daysOfWeek: "Monday", interval: 1);
        var biWeeklyShift = TestDbHelpers.CreateShift(daysOfWeek: "Monday", interval: 2);

        var weeklyMatch = AttendanceService.IsShiftScheduledOnDate(weeklyShift, new DateTime(2025, 1, 13));
        var biWeeklyActiveWeek = AttendanceService.IsShiftScheduledOnDate(biWeeklyShift, new DateTime(2025, 1, 6));
        var biWeeklyInactiveWeek = AttendanceService.IsShiftScheduledOnDate(biWeeklyShift, new DateTime(2025, 1, 13));

        weeklyMatch.Should().BeTrue();
        biWeeklyActiveWeek.Should().BeTrue();
        biWeeklyInactiveWeek.Should().BeFalse();
    }
}
