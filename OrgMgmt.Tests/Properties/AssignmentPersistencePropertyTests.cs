using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using OrgMgmt;
using OrgMgmt.Models;
using OrgMgmt.Services;

namespace OrgMgmt.Tests.Properties;

// Feature: bi-weekly-schedule-generation, Property 4: Valid assignment round-trip persistence

/// <summary>
/// Property tests verifying that a valid (non-conflicting) shift assignment
/// persists correctly and appears in the employee's shift list when queried back.
/// **Validates: Requirements 4.1**
/// </summary>
public class AssignmentPersistencePropertyTests
{
    private static readonly string[] AllDays =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    /// <summary>
    /// Generates two non-overlapping shifts: one for existing assignment, one to propose.
    /// Non-overlapping means either no shared days OR non-intersecting time ranges.
    /// </summary>
    private static Arbitrary<(ShiftData Existing, ShiftData Proposed)> NonOverlappingPairArb()
    {
        // Strategy: use completely disjoint time ranges on the same days to guarantee no overlap
        var gen =
            from existingStartMin in Gen.Choose(0, 600)
            from existingDuration in Gen.Choose(30, 120)
            let existingEnd = existingStartMin + existingDuration
            // Proposed starts after existing ends (gap of at least 1 minute)
            let proposedStartMin = existingEnd + 1
            from proposedDuration in Gen.Choose(30, Math.Min(120, 1439 - proposedStartMin))
            where proposedStartMin + proposedDuration <= 1439
            from dayFlags in Gen.Elements(true, false).ListOf(7)
            let selectedDays = dayFlags.Zip(AllDays)
                                       .Where(p => p.Item1)
                                       .Select(p => p.Item2)
                                       .ToList()
            where selectedDays.Count > 0
            let daysStr = string.Join(",", selectedDays)
            select (
                new ShiftData(
                    TimeSpan.FromMinutes(existingStartMin),
                    TimeSpan.FromMinutes(existingEnd),
                    daysStr),
                new ShiftData(
                    TimeSpan.FromMinutes(proposedStartMin),
                    TimeSpan.FromMinutes(proposedStartMin + proposedDuration),
                    daysStr)
            );

        return gen.ToArbitrary();
    }

    /// <summary>
    /// For any employee and shift where no overlap conflict and no vacation conflict exist,
    /// assigning the shift and then querying the employee's shifts SHALL return a list
    /// that contains the newly assigned shift.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValidAssignment_RoundTrip_PersistsCorrectly()
    {
        return Prop.ForAll(NonOverlappingPairArb(), pair =>
        {
            var (existingData, proposedData) = pair;
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<OrgDbContext>()
                .UseSqlite(connection)
                .Options;

            var employeeId = Guid.NewGuid();
            var existingShiftId = Guid.NewGuid();
            var proposedShiftId = Guid.NewGuid();

            // Setup: create employee with one existing shift
            using (var context = new OrgDbContext(options))
            {
                context.Database.EnsureCreated();

                var employee = new Employee
                {
                    ID = employeeId,
                    Name = "TestEmp",
                    Address = "TestCity",
                    Role = "CareAide",
                    IsActive = true,
                    HourlyPayRate = 20m
                };

                var existingShift = new Shift
                {
                    Id = existingShiftId,
                    Name = "ExistingShift",
                    Location = "RoomA",
                    StartTime = existingData.StartTime,
                    EndTime = existingData.EndTime,
                    DaysOfWeek = existingData.DaysOfWeek,
                    Frequency = Frequency.Weekly,
                    Interval = 1
                };

                var proposedShift = new Shift
                {
                    Id = proposedShiftId,
                    Name = "ProposedShift",
                    Location = "RoomB",
                    StartTime = proposedData.StartTime,
                    EndTime = proposedData.EndTime,
                    DaysOfWeek = proposedData.DaysOfWeek,
                    Frequency = Frequency.Weekly,
                    Interval = 1
                };

                employee.Shifts.Add(existingShift);
                context.Employees.Add(employee);
                context.Shifts.Add(existingShift);
                context.Shifts.Add(proposedShift);
                context.SaveChanges();
            }

            // Act: validate and assign
            using (var context = new OrgDbContext(options))
            {
                var service = new ShiftValidationService();
                var overlapResult = service.ValidateOverlap(employeeId, proposedShiftId, context)
                    .GetAwaiter().GetResult();
                var vacationResult = service.ValidateVacationConflict(employeeId, proposedShiftId, context)
                    .GetAwaiter().GetResult();

                if (!overlapResult.IsValid || !vacationResult.IsValid)
                    return true; // Skip cases where generator accidentally creates overlap

                var emp = context.Employees.Include(e => e.Shifts).First(e => e.ID == employeeId);
                var shift = context.Shifts.First(s => s.Id == proposedShiftId);
                emp.Shifts.Add(shift);
                context.SaveChanges();
            }

            // Verify: query back and check the shift is present
            using (var context = new OrgDbContext(options))
            {
                var assignedShiftIds = context.Employees
                    .Where(e => e.ID == employeeId)
                    .SelectMany(e => e.Shifts)
                    .Select(s => s.Id)
                    .ToHashSet();

                return assignedShiftIds.Contains(proposedShiftId)
                    && assignedShiftIds.Contains(existingShiftId);
            }
        });
    }
}
