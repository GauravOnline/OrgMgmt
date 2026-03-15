using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using OrgMgmt;
using OrgMgmt.Models;
using OrgMgmt.ViewModels;

namespace OrgMgmt.Tests.Properties;

// Feature: bi-weekly-schedule-generation, Property 8: Financial data exclusion from view models

/// <summary>
/// Property tests verifying that EmployeeListItem never exposes financial data.
/// **Validates: Requirements 1.4, 7.3**
/// </summary>
public class FinancialDataExclusionPropertyTests
{
    private static readonly string[] FinancialFieldNames =
        ["HourlyPayRate", "Salary", "PayRate", "Wage", "Compensation", "Pay"];

    /// <summary>
    /// For any Employee entity, the projected EmployeeListItem SHALL contain
    /// only Id, Name, and Role — no HourlyPayRate or financial fields.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EmployeeListItem_ExcludesFinancialData()
    {
        var gen =
            from payRate in Gen.Choose(1, 10000).Select(x => (decimal)x / 100m)
            from nameLen in Gen.Choose(1, 10)
            from nameChars in Gen.Elements('A', 'B', 'C', 'D', 'E').ListOf(nameLen)
            from roleLen in Gen.Choose(1, 8)
            from roleChars in Gen.Elements('X', 'Y', 'Z').ListOf(roleLen)
            select new Employee
            {
                ID = Guid.NewGuid(),
                Name = new string(nameChars.ToArray()),
                Address = "TestCity",
                Role = new string(roleChars.ToArray()),
                IsActive = true,
                HourlyPayRate = payRate
            };

        return Prop.ForAll(gen.ToArbitrary(), employee =>
        {
            // Project to EmployeeListItem (same projection the controller will use)
            var item = new EmployeeListItem
            {
                Id = employee.ID,
                Name = employee.Name,
                Role = employee.Role
            };

            // Verify the view model type has no financial properties via reflection
            var properties = typeof(EmployeeListItem).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var propertyNames = properties.Select(p => p.Name).ToList();

            bool noFinancialFields = !propertyNames.Any(name =>
                FinancialFieldNames.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));

            // Verify only expected fields exist
            var expectedFields = new HashSet<string> { "Id", "Name", "Role" };
            bool onlyExpectedFields = propertyNames.All(n => expectedFields.Contains(n));

            // Verify projected values match source (minus financial data)
            bool valuesCorrect = item.Id == employee.ID
                              && item.Name == employee.Name
                              && item.Role == employee.Role;

            return noFinancialFields && onlyExpectedFields && valuesCorrect;
        });
    }
}

// Feature: bi-weekly-schedule-generation, Property 6: Shift view model completeness with interval labels

/// <summary>
/// Property tests verifying ShiftAssignmentItem correctly maps all fields
/// and computes IntervalLabel based on Interval value.
/// **Validates: Requirements 5.1, 5.2**
/// </summary>
public class ShiftViewModelCompletenessPropertyTests
{
    private static readonly string[] AllDays =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    /// <summary>
    /// For any shift with Interval 1 or 2, the mapped ShiftAssignmentItem SHALL
    /// include all fields and IntervalLabel SHALL be "Weekly" or "Bi-weekly" accordingly.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ShiftAssignmentItem_HasCorrectIntervalLabel()
    {
        var gen =
            from interval in Gen.Elements(1, 2)
            from startMinutes in Gen.Choose(0, 1380)
            from duration in Gen.Choose(30, 1439 - startMinutes)
            from dayFlags in Gen.Elements(true, false).ListOf(7)
            let selectedDays = dayFlags.Zip(AllDays)
                                       .Where(p => p.Item1)
                                       .Select(p => p.Item2)
                                       .ToList()
            where selectedDays.Count > 0
            from nameLen in Gen.Choose(1, 10)
            from nameChars in Gen.Elements('A', 'B', 'C', 'D').ListOf(nameLen)
            from locLen in Gen.Choose(1, 8)
            from locChars in Gen.Elements('R', 'o', 'm').ListOf(locLen)
            select new Shift
            {
                Id = Guid.NewGuid(),
                Name = new string(nameChars.ToArray()),
                Location = new string(locChars.ToArray()),
                StartTime = TimeSpan.FromMinutes(startMinutes),
                EndTime = TimeSpan.FromMinutes(startMinutes + duration),
                DaysOfWeek = string.Join(",", selectedDays),
                Frequency = Frequency.Weekly,
                Interval = interval
            };

        return Prop.ForAll(gen.ToArbitrary(), shift =>
        {
            // Map to ShiftAssignmentItem (same mapping the controller will use)
            var item = new ShiftAssignmentItem
            {
                ShiftId = shift.Id,
                Name = shift.Name,
                Location = shift.Location,
                StartTime = shift.StartTime,
                EndTime = shift.EndTime,
                DaysOfWeek = shift.DaysOfWeek ?? string.Empty,
                Interval = shift.Interval
            };

            // Verify all fields are mapped correctly
            bool fieldsCorrect = item.ShiftId == shift.Id
                              && item.Name == shift.Name
                              && item.Location == shift.Location
                              && item.StartTime == shift.StartTime
                              && item.EndTime == shift.EndTime
                              && item.DaysOfWeek == (shift.DaysOfWeek ?? string.Empty)
                              && item.Interval == shift.Interval;

            // Verify IntervalLabel
            string expectedLabel = shift.Interval == 2 ? "Bi-weekly" : "Weekly";
            bool labelCorrect = item.IntervalLabel == expectedLabel;

            return fieldsCorrect && labelCorrect;
        });
    }
}

// Feature: bi-weekly-schedule-generation, Property 9: Only active employees in assignment list

/// <summary>
/// Property tests verifying that only active employees appear in the assignment list.
/// **Validates: Requirements 8.1, 8.2**
/// </summary>
public class ActiveEmployeesOnlyPropertyTests
{
    private static OrgDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<OrgDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new OrgDbContext(options);
    }

    /// <summary>
    /// For any set of employees with mixed IsActive values, the employee list
    /// returned for the assignment page SHALL contain only employees where IsActive is true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AssignmentList_ContainsOnlyActiveEmployees()
    {
        var employeeGen =
            from nameLen in Gen.Choose(1, 10)
            from nameChars in Gen.Elements('A', 'B', 'C', 'D', 'E').ListOf(nameLen)
            from roleLen in Gen.Choose(1, 8)
            from roleChars in Gen.Elements('X', 'Y', 'Z').ListOf(roleLen)
            from isActive in Gen.Elements(true, false)
            from payRate in Gen.Choose(1, 10000).Select(x => (decimal)x / 100m)
            select new Employee
            {
                ID = Guid.NewGuid(),
                Name = new string(nameChars.ToArray()),
                Address = "TestCity",
                Role = new string(roleChars.ToArray()),
                IsActive = isActive,
                HourlyPayRate = payRate
            };

        var listGen =
            from count in Gen.Choose(1, 20)
            from emps in employeeGen.ListOf(count)
            select emps;

        return Prop.ForAll(listGen.ToArbitrary(), employees =>
        {
            var employeeList = employees.ToList();
            var dbName = $"ActiveOnlyTest_{Guid.NewGuid()}";
            using var context = CreateInMemoryContext(dbName);

            foreach (var emp in employeeList)
                context.Employees.Add(emp);
            context.SaveChanges();

            // Replicate the same query the ScheduleController.Assign() uses
            var result = context.Employees
                .Where(e => e.IsActive)
                .Select(e => new EmployeeListItem
                {
                    Id = e.ID,
                    Name = e.Name,
                    Role = e.Role
                })
                .ToList();

            // All returned employees must be active
            var activeIds = employeeList.Where(e => e.IsActive).Select(e => e.ID).ToHashSet();
            bool allActive = result.All(r => activeIds.Contains(r.Id));

            // Count must match the number of active employees seeded
            bool countCorrect = result.Count == activeIds.Count;

            return allActive && countCorrect;
        });
    }
}
