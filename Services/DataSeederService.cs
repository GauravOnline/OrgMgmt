using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;

namespace OrgMgmt.Services
{
    public static class DataSeederService
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<OrgDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // Skip if data already exists
            if (await context.Employees.AnyAsync())
                return;

            // --- Employees ---
            var employees = new List<Employee>
            {
                new() { Name = "Alice Johnson", Address = "Vancouver", DateOfBirth = new DateTime(1988, 3, 15), Role = "Nurse", IsActive = true, HourlyPayRate = 42.00m },
                new() { Name = "Bob Smith", Address = "Burnaby", DateOfBirth = new DateTime(1992, 7, 22), Role = "Care Aide", IsActive = true, HourlyPayRate = 28.50m },
                new() { Name = "Carol Davis", Address = "Surrey", DateOfBirth = new DateTime(1985, 11, 8), Role = "Nurse", IsActive = true, HourlyPayRate = 45.00m },
                new() { Name = "David Lee", Address = "Richmond", DateOfBirth = new DateTime(1990, 1, 30), Role = "Care Aide", IsActive = true, HourlyPayRate = 27.00m },
                new() { Name = "Eva Martinez", Address = "Coquitlam", DateOfBirth = new DateTime(1995, 5, 12), Role = "Care Aide", IsActive = true, HourlyPayRate = 26.50m },
                new() { Name = "Frank Wilson", Address = "Vancouver", DateOfBirth = new DateTime(1982, 9, 3), Role = "Nurse", IsActive = false, HourlyPayRate = 44.00m },
            };
            context.Employees.AddRange(employees);
            await context.SaveChangesAsync();

            // --- Shifts ---
            var morningWardA = new Shift { Name = "Morning Shift", Location = "Ward A", StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(15, 0, 0), Frequency = Frequency.Weekly, Interval = 1, DaysOfWeek = "Monday,Tuesday,Wednesday,Thursday,Friday" };
            var eveningWardA = new Shift { Name = "Evening Shift", Location = "Ward A", StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(23, 0, 0), Frequency = Frequency.Weekly, Interval = 1, DaysOfWeek = "Monday,Tuesday,Wednesday,Thursday,Friday" };
            var nightWardB = new Shift { Name = "Night Shift", Location = "Ward B", StartTime = new TimeSpan(23, 0, 0), EndTime = new TimeSpan(7, 0, 0), Frequency = Frequency.Weekly, Interval = 2, DaysOfWeek = "Monday,Wednesday,Friday" };
            var weekendWardA = new Shift { Name = "Weekend Day", Location = "Ward A", StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(16, 0, 0), Frequency = Frequency.Weekly, Interval = 2, DaysOfWeek = "Saturday,Sunday" };
            var morningWardB = new Shift { Name = "Morning Shift", Location = "Ward B", StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(15, 0, 0), Frequency = Frequency.Weekly, Interval = 1, DaysOfWeek = "Tuesday,Thursday" };

            context.Shifts.AddRange(morningWardA, eveningWardA, nightWardB, weekendWardA, morningWardB);
            await context.SaveChangesAsync();

            // --- Assign shifts to employees ---
            employees[0].Shifts.Add(morningWardA);  // Alice - Morning Ward A
            employees[1].Shifts.Add(eveningWardA);  // Bob - Evening Ward A
            employees[2].Shifts.Add(morningWardB);  // Carol - Morning Ward B
            employees[3].Shifts.Add(weekendWardA);  // David - Weekend Ward A
            employees[4].Shifts.Add(morningWardA);  // Eva - Morning Ward A
            await context.SaveChangesAsync();

            // --- Clients ---
            var clients = new List<Client>
            {
                new() { Name = "Margaret Brown", Address = "Vancouver", DateOfBirth = new DateTime(1940, 6, 20), Balance = 1500.00m },
                new() { Name = "Robert Taylor", Address = "Burnaby", DateOfBirth = new DateTime(1935, 2, 14), Balance = 2200.00m },
                new() { Name = "Helen White", Address = "Surrey", DateOfBirth = new DateTime(1942, 10, 5), Balance = 800.00m },
            };
            context.Clients.AddRange(clients);
            await context.SaveChangesAsync();

            // --- Services ---
            var services = new List<Service>
            {
                new() { Type = "Daily Care", Rate = 150.00m, EmployeeId = employees[0].ID },
                new() { Type = "Physical Therapy", Rate = 200.00m, EmployeeId = employees[2].ID },
                new() { Type = "Medication Admin", Rate = 75.00m, EmployeeId = employees[0].ID },
            };
            services[0].Clients.Add(clients[0]);
            services[0].Clients.Add(clients[1]);
            services[1].Clients.Add(clients[2]);
            services[2].Clients.Add(clients[0]);
            context.Services.AddRange(services);
            await context.SaveChangesAsync();

            // --- Attendance Records (last 2 weeks) ---
            var today = DateTime.Today;
            var periodStart = today.AddDays(-13);
            var records = new List<AttendanceRecord>();

            // Generate attendance for Alice (Morning Ward A, weekdays)
            for (var d = periodStart; d <= today; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var adj = AdjustmentType.None;
                decimal hours = 8.0m;
                DateTime? clockIn = d.Add(new TimeSpan(7, 0, 0));
                DateTime? clockOut = d.Add(new TimeSpan(15, 0, 0));

                // Simulate a sick day and a late arrival
                if (d == periodStart.AddDays(2)) { adj = AdjustmentType.Sick; hours = 0; clockIn = null; clockOut = null; }
                if (d == periodStart.AddDays(7)) { adj = AdjustmentType.Late; hours = 7.5m; clockIn = d.Add(new TimeSpan(7, 30, 0)); }

                records.Add(new AttendanceRecord { EmployeeId = employees[0].ID, ShiftId = morningWardA.Id, TargetDate = d, ClockInTime = clockIn, ClockOutTime = clockOut, HoursToPay = hours, AdjustmentType = adj });
            }

            // Generate attendance for Bob (Evening Ward A, weekdays)
            for (var d = periodStart; d <= today; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var adj = AdjustmentType.None;
                decimal hours = 8.0m;
                DateTime? clockIn = d.Add(new TimeSpan(15, 0, 0));
                DateTime? clockOut = d.Add(new TimeSpan(23, 0, 0));

                if (d == periodStart.AddDays(4)) { adj = AdjustmentType.NoShow; hours = 0; clockIn = null; clockOut = null; }

                records.Add(new AttendanceRecord { EmployeeId = employees[1].ID, ShiftId = eveningWardA.Id, TargetDate = d, ClockInTime = clockIn, ClockOutTime = clockOut, HoursToPay = hours, AdjustmentType = adj });
            }

            // Add a vacation record for Eva (upcoming)
            records.Add(new AttendanceRecord { EmployeeId = employees[4].ID, ShiftId = morningWardA.Id, TargetDate = today.AddDays(3), HoursToPay = 0, AdjustmentType = AdjustmentType.Vacation, Notes = "Approved vacation" });

            context.AttendanceRecords.AddRange(records);
            await context.SaveChangesAsync();

            // --- User accounts with roles ---
            await CreateUserWithRole(userManager, "hr@orgmgmt.local", "Staff123!", "HR");
            await CreateUserWithRole(userManager, "payroll@orgmgmt.local", "Staff123!", "Payroll");
            await CreateUserWithRole(userManager, "scheduler@orgmgmt.local", "Staff123!", "ScheduleManager");
            await CreateUserWithRole(userManager, "manager@orgmgmt.local", "Staff123!", "DirectManager");
            await CreateUserWithRole(userManager, "employee@orgmgmt.local", "Staff123!", "Employee");
        }

        private static async Task CreateUserWithRole(UserManager<ApplicationUser> userManager, string email, string password, string role)
        {
            if (await userManager.FindByEmailAsync(email) != null) return;
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
                await userManager.AddToRoleAsync(user, role);
        }
    }
}
