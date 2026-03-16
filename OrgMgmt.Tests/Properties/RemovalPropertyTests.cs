using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using OrgMgmt;
using OrgMgmt.Models;

namespace OrgMgmt.Tests.Properties;

// Feature: bi-weekly-schedule-generation, Property 7: Removal deletes assignment

/// <summary>
/// Property tests verifying that removing a shift assignment deletes the
/// EmployeeShift record and the shift no longer appears in the employee's list.
/// **Validates: Requirements 6.2**
/// </summary>
public class RemovalPropertyTests
{
    private static readonly string[] AllDays =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    /// <summary>
    /// For any employee-shift assignment that exists, after removal the employee's
    /// shift list SHALL no longer contain that shift and the EmployeeShift record
    /// SHALL no longer exist.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Removal_DeletesAssignment()
    {
        var gen =
            from startMin in Gen.Choose(0, 1380)
            from duration in Gen.Choose(30, 1439 - startMin)
            from dayFlags in Gen.Elements(true, false).ListOf(7)
            let selectedDays = dayFlags.Zip(AllDays)
                                       .Where(p => p.Item1)
                                       .Select(p => p.Item2)
                                       .ToList()
            where selectedDays.Count > 0
            from interval in Gen.Elements(1, 2)
            select (
                StartTime: TimeSpan.FromMinutes(startMin),
                EndTime: TimeSpan.FromMinutes(startMin + duration),
                Days: string.Join(",", selectedDays),
                Interval: interval
            );

        return Prop.ForAll(gen.ToArbitrary(), data =>
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<OrgDbContext>()
                .UseSqlite(connection)
                .Options;

            var employeeId = Guid.NewGuid();
            var shiftId = Guid.NewGuid();

            // Setup: create employee with an assigned shift
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

                var shift = new Shift
                {
                    Id = shiftId,
                    Name = "TestShift",
                    Location = "RoomA",
                    StartTime = data.StartTime,
                    EndTime = data.EndTime,
                    DaysOfWeek = data.Days,
                    Frequency = Frequency.Weekly,
                    Interval = data.Interval
                };

                employee.Shifts.Add(shift);
                context.Employees.Add(employee);
                context.Shifts.Add(shift);
                context.SaveChanges();
            }

            // Verify assignment exists before removal
            using (var context = new OrgDbContext(options))
            {
                var before = context.Employees
                    .Where(e => e.ID == employeeId)
                    .SelectMany(e => e.Shifts)
                    .Any(s => s.Id == shiftId);
                if (!before) return false;
            }

            // Act: remove the assignment
            using (var context = new OrgDbContext(options))
            {
                var emp = context.Employees.Include(e => e.Shifts).First(e => e.ID == employeeId);
                var shift = emp.Shifts.First(s => s.Id == shiftId);
                emp.Shifts.Remove(shift);
                context.SaveChanges();
            }

            // Verify: shift no longer in employee's list
            using (var context = new OrgDbContext(options))
            {
                var stillAssigned = context.Employees
                    .Where(e => e.ID == employeeId)
                    .SelectMany(e => e.Shifts)
                    .Any(s => s.Id == shiftId);

                // Also verify the shift entity itself still exists (only the join record was removed)
                var shiftExists = context.Shifts.Any(s => s.Id == shiftId);

                return !stillAssigned && shiftExists;
            }
        });
    }
}
