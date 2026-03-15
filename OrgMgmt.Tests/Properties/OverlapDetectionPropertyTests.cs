using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using OrgMgmt;
using OrgMgmt.Models;
using OrgMgmt.Services;

namespace OrgMgmt.Tests.Properties;

// Feature: bi-weekly-schedule-generation, Property 1: Overlap detection correctness

/// <summary>
/// Represents a generated shift's time and day data for property testing.
/// </summary>
public record ShiftData(TimeSpan StartTime, TimeSpan EndTime, string DaysOfWeek);

/// <summary>
/// Custom Arbitrary provider for shift data used in overlap detection tests.
/// </summary>
public class ShiftDataArbitrary
{
    private static readonly string[] AllDays =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public static Arbitrary<ShiftData> ShiftDataArb()
    {
        var gen =
            from startMinutes in Gen.Choose(0, 1438)
            from duration in Gen.Choose(1, 1439 - startMinutes)
            from dayFlags in Gen.Elements(true, false).ListOf(7)
            let selectedDays = dayFlags.Zip(AllDays)
                                       .Where(p => p.Item1)
                                       .Select(p => p.Item2)
                                       .ToList()
            where selectedDays.Count > 0
            select new ShiftData(
                TimeSpan.FromMinutes(startMinutes),
                TimeSpan.FromMinutes(startMinutes + duration),
                string.Join(",", selectedDays));

        return gen.ToArbitrary();
    }
}

/// <summary>
/// Property tests for overlap detection correctness.
/// **Validates: Requirements 2.2**
/// </summary>
public class OverlapDetectionPropertyTests
{
    private static OrgDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<OrgDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new OrgDbContext(options);
    }

    private static bool ExpectedOverlap(ShiftData existing, ShiftData proposed)
    {
        // Days overlap check
        var setA = new HashSet<string>(
            existing.DaysOfWeek.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(
            proposed.DaysOfWeek.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
        bool sharedDays = setA.Overlaps(setB);

        // Time range intersection check
        bool timesIntersect = existing.StartTime < proposed.EndTime
                           && proposed.StartTime < existing.EndTime;

        return sharedDays && timesIntersect;
    }

    /// <summary>
    /// For any two shifts with arbitrary StartTime, EndTime, and DaysOfWeek values,
    /// the overlap detection function returns true iff the two shifts share at least
    /// one common day AND their time ranges intersect.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ShiftDataArbitrary) })]
    public bool OverlapDetection_IsCorrect(ShiftData existingData, ShiftData proposedData)
    {
        var dbName = $"OverlapTest_{Guid.NewGuid()}";
        using var context = CreateInMemoryContext(dbName);

        // Create employee
        var employee = new Employee
        {
            ID = Guid.NewGuid(),
            Name = "TestEmp",
            Address = "TestCity",
            Role = "CareAide",
            IsActive = true,
            HourlyPayRate = 20m
        };

        // Create existing shift assigned to employee
        var existingShift = new Shift
        {
            Id = Guid.NewGuid(),
            Name = "ExistingShift",
            Location = "RoomA",
            StartTime = existingData.StartTime,
            EndTime = existingData.EndTime,
            DaysOfWeek = existingData.DaysOfWeek,
            Frequency = Frequency.Weekly,
            Interval = 1
        };

        employee.Shifts.Add(existingShift);
        context.Employees.Add(employee);
        context.Shifts.Add(existingShift);

        // Create proposed shift
        var proposedShift = new Shift
        {
            Id = Guid.NewGuid(),
            Name = "ProposedShift",
            Location = "RoomB",
            StartTime = proposedData.StartTime,
            EndTime = proposedData.EndTime,
            DaysOfWeek = proposedData.DaysOfWeek,
            Frequency = Frequency.Weekly,
            Interval = 1
        };
        context.Shifts.Add(proposedShift);
        context.SaveChanges();

        // Run validation
        var service = new ShiftValidationService();
        var result = service.ValidateOverlap(employee.ID, proposedShift.Id, context)
                            .GetAwaiter().GetResult();

        // Compute expected: overlap detected means IsValid should be false
        bool expectedOverlap = ExpectedOverlap(existingData, proposedData);
        bool actualOverlapDetected = !result.IsValid;

        return actualOverlapDetected == expectedOverlap;
    }
}

// Feature: bi-weekly-schedule-generation, Property 2: Overlapping assignments are blocked

/// <summary>
/// Property tests verifying that overlapping shift assignments are blocked
/// and the employee's shift list remains unchanged after rejection.
/// **Validates: Requirements 2.3, 2.4**
/// </summary>
public class OverlappingAssignmentsBlockedPropertyTests
{
    private static OrgDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<OrgDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new OrgDbContext(options);
    }

    /// <summary>
    /// Generates a proposed shift that is guaranteed to overlap with the existing shift
    /// by sharing at least one common day and having intersecting time ranges.
    /// </summary>
    private static Arbitrary<(ShiftData Existing, ShiftData Proposed)> OverlappingPairArb()
    {
        string[] allDays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

        var gen =
            from existingStartMin in Gen.Choose(0, 1380)
            from existingDuration in Gen.Choose(30, 1439 - existingStartMin)
            let existingEnd = existingStartMin + existingDuration
            // Proposed start must be before existing end, proposed end must be after existing start
            from proposedStartMin in Gen.Choose(Math.Max(0, existingStartMin - 300), existingEnd - 1)
            from proposedDuration in Gen.Choose(1, Math.Min(600, 1439 - proposedStartMin))
            let proposedEnd = proposedStartMin + proposedDuration
            where proposedStartMin < existingEnd && existingStartMin < proposedEnd
            // Pick a shared day, then optionally add more days to each
            from sharedDayIdx in Gen.Choose(0, 6)
            from extraExistingFlags in Gen.Elements(true, false).ListOf(7)
            from extraProposedFlags in Gen.Elements(true, false).ListOf(7)
            let existingDays = extraExistingFlags
                .Select((f, i) => (f, i))
                .Where(p => p.f || p.i == sharedDayIdx)
                .Select(p => allDays[p.i])
                .ToList()
            let proposedDays = extraProposedFlags
                .Select((f, i) => (f, i))
                .Where(p => p.f || p.i == sharedDayIdx)
                .Select(p => allDays[p.i])
                .ToList()
            select (
                new ShiftData(TimeSpan.FromMinutes(existingStartMin), TimeSpan.FromMinutes(existingEnd), string.Join(",", existingDays)),
                new ShiftData(TimeSpan.FromMinutes(proposedStartMin), TimeSpan.FromMinutes(proposedEnd), string.Join(",", proposedDays))
            );

        return gen.ToArbitrary();
    }

    /// <summary>
    /// For any employee with an existing shift assignment and any proposed shift that
    /// overlaps, the system rejects the assignment and the employee's shift list remains unchanged.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OverlappingAssignment_IsBlocked_And_ShiftListUnchanged()
    {
        return Prop.ForAll(OverlappingPairArb(), pair =>
        {
            var (existingData, proposedData) = pair;
            var dbName = $"OverlapBlockedTest_{Guid.NewGuid()}";
            using var context = CreateInMemoryContext(dbName);

            // Arrange: create employee with one existing shift
            var employee = new Employee
            {
                ID = Guid.NewGuid(),
                Name = "TestEmp",
                Address = "TestAddr",
                Role = "CareAide",
                IsActive = true,
                HourlyPayRate = 20m
            };

            var existingShift = new Shift
            {
                Id = Guid.NewGuid(),
                Name = "ExistingShift",
                Location = "RoomA",
                StartTime = existingData.StartTime,
                EndTime = existingData.EndTime,
                DaysOfWeek = existingData.DaysOfWeek,
                Frequency = Frequency.Weekly,
                Interval = 1
            };

            employee.Shifts.Add(existingShift);
            context.Employees.Add(employee);
            context.Shifts.Add(existingShift);

            var proposedShift = new Shift
            {
                Id = Guid.NewGuid(),
                Name = "ProposedShift",
                Location = "RoomB",
                StartTime = proposedData.StartTime,
                EndTime = proposedData.EndTime,
                DaysOfWeek = proposedData.DaysOfWeek,
                Frequency = Frequency.Weekly,
                Interval = 1
            };
            context.Shifts.Add(proposedShift);
            context.SaveChanges();

            // Snapshot the shift list before attempting assignment
            var shiftIdsBefore = context.Employees
                .Where(e => e.ID == employee.ID)
                .SelectMany(e => e.Shifts)
                .Select(s => s.Id)
                .OrderBy(id => id)
                .ToList();

            // Act: validate overlap
            var service = new ShiftValidationService();
            var result = service.ValidateOverlap(employee.ID, proposedShift.Id, context)
                                .GetAwaiter().GetResult();

            // Simulate: only add if validation passes (it shouldn't)
            if (result.IsValid)
            {
                var emp = context.Employees.Include(e => e.Shifts).First(e => e.ID == employee.ID);
                emp.Shifts.Add(proposedShift);
                context.SaveChanges();
            }

            // Snapshot the shift list after
            var shiftIdsAfter = context.Employees
                .Where(e => e.ID == employee.ID)
                .SelectMany(e => e.Shifts)
                .Select(s => s.Id)
                .OrderBy(id => id)
                .ToList();

            // Assert: assignment was rejected and shift list is unchanged
            bool rejected = !result.IsValid;
            bool listUnchanged = shiftIdsBefore.SequenceEqual(shiftIdsAfter);

            return rejected && listUnchanged;
        });
    }
}


// Feature: bi-weekly-schedule-generation, Property 3: Vacation conflict blocks assignment

/// <summary>
/// Property tests verifying that vacation conflicts block shift assignment
/// and the employee's shift list remains unchanged after rejection.
/// **Validates: Requirements 3.2, 3.3**
/// </summary>
public class VacationConflictBlocksAssignmentPropertyTests
{
    private static OrgDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<OrgDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new OrgDbContext(options);
    }

    private static readonly string[] AllDays =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    /// <summary>
    /// Custom generator that produces:
    /// - A shift interval (1 = weekly, 2 = bi-weekly)
    /// - A set of shift days
    /// - A vacation TargetDate within the recurrence window whose day-of-week matches one of the shift days
    /// </summary>
    private static Arbitrary<(int Interval, string ShiftDays, DateTime VacationDate)> VacationConflictArb()
    {
        var gen =
            from interval in Gen.Elements(1, 2)
            let windowDays = interval == 2 ? 14 : 7
            // Pick a vacation date within the recurrence window [today, today + windowDays)
            from dayOffset in Gen.Choose(0, windowDays - 1)
            let vacationDate = DateTime.Today.AddDays(dayOffset)
            let vacationDayName = vacationDate.DayOfWeek.ToString()
            // Build shift days that include the vacation day, plus optionally more days
            from extraFlags in Gen.Elements(true, false).ListOf(7)
            let shiftDays = extraFlags
                .Select((f, i) => (f, i))
                .Where(p => p.f || AllDays[p.i].Equals(vacationDayName, StringComparison.OrdinalIgnoreCase))
                .Select(p => AllDays[p.i])
                .Distinct()
                .ToList()
            where shiftDays.Count > 0
            select (interval, string.Join(",", shiftDays), vacationDate);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// For any employee with a vacation AttendanceRecord whose TargetDate falls within
    /// the recurrence window and whose day-of-week is in the proposed shift's DaysOfWeek,
    /// the system rejects the assignment and the employee's shift list remains unchanged.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property VacationConflict_BlocksAssignment_And_ShiftListUnchanged()
    {
        return Prop.ForAll(VacationConflictArb(), data =>
        {
            var (interval, shiftDays, vacationDate) = data;
            var dbName = $"VacationConflictTest_{Guid.NewGuid()}";
            using var context = CreateInMemoryContext(dbName);

            // Arrange: create employee
            var employee = new Employee
            {
                ID = Guid.NewGuid(),
                Name = "TestEmp",
                Address = "TestCity",
                Role = "CareAide",
                IsActive = true,
                HourlyPayRate = 20m
            };
            context.Employees.Add(employee);

            // Create a dummy shift to satisfy the AttendanceRecord FK
            var dummyShift = new Shift
            {
                Id = Guid.NewGuid(),
                Name = "DummyShift",
                Location = "RoomX",
                StartTime = TimeSpan.FromHours(8),
                EndTime = TimeSpan.FromHours(16),
                DaysOfWeek = "Monday",
                Frequency = Frequency.Weekly,
                Interval = 1
            };
            context.Shifts.Add(dummyShift);

            // Create vacation attendance record on the conflicting date
            var vacationRecord = new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.ID,
                ShiftId = dummyShift.Id,
                TargetDate = vacationDate,
                AdjustmentType = AdjustmentType.Vacation,
                HoursToPay = 0
            };
            context.AttendanceRecords.Add(vacationRecord);

            // Create proposed shift with days that include the vacation day
            var proposedShift = new Shift
            {
                Id = Guid.NewGuid(),
                Name = "ProposedShift",
                Location = "RoomA",
                StartTime = TimeSpan.FromHours(9),
                EndTime = TimeSpan.FromHours(17),
                DaysOfWeek = shiftDays,
                Frequency = Frequency.Weekly,
                Interval = interval
            };
            context.Shifts.Add(proposedShift);
            context.SaveChanges();

            // Snapshot shift list before
            var shiftIdsBefore = context.Employees
                .Where(e => e.ID == employee.ID)
                .SelectMany(e => e.Shifts)
                .Select(s => s.Id)
                .OrderBy(id => id)
                .ToList();

            // Act: validate vacation conflict
            var service = new ShiftValidationService();
            var result = service.ValidateVacationConflict(employee.ID, proposedShift.Id, context)
                                .GetAwaiter().GetResult();

            // Simulate: only add if validation passes (it shouldn't)
            if (result.IsValid)
            {
                var emp = context.Employees.Include(e => e.Shifts).First(e => e.ID == employee.ID);
                emp.Shifts.Add(proposedShift);
                context.SaveChanges();
            }

            // Snapshot shift list after
            var shiftIdsAfter = context.Employees
                .Where(e => e.ID == employee.ID)
                .SelectMany(e => e.Shifts)
                .Select(s => s.Id)
                .OrderBy(id => id)
                .ToList();

            // Assert: assignment was rejected and shift list is unchanged
            bool rejected = !result.IsValid;
            bool listUnchanged = shiftIdsBefore.SequenceEqual(shiftIdsAfter);

            return rejected && listUnchanged;
        });
    }
}

