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

            if (await context.Employees.AnyAsync())
                return;

            // ── Employees ──
            // 5 active + 1 inactive to test IsActive filtering (Req: attendance 1.5, schedule 8.1)
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

            // ── Shifts ──
            // Weekly weekday shifts (attendance dashboard testing)
            var morningWardA = new Shift { Name = "Morning Shift", Location = "Ward A", StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(15, 0, 0), Frequency = Frequency.Weekly, Interval = 1, DaysOfWeek = "Monday,Tuesday,Wednesday,Thursday,Friday" };
            var eveningWardA = new Shift { Name = "Evening Shift", Location = "Ward A", StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(23, 0, 0), Frequency = Frequency.Weekly, Interval = 1, DaysOfWeek = "Monday,Tuesday,Wednesday,Thursday,Friday" };
            // Bi-weekly shift (schedule recurrence testing, Req: schedule 1.2)
            var nightWardB = new Shift { Name = "Night Shift", Location = "Ward B", StartTime = new TimeSpan(23, 0, 0), EndTime = new TimeSpan(7, 0, 0), Frequency = Frequency.Weekly, Interval = 2, DaysOfWeek = "Monday,Wednesday,Friday" };
            // Bi-weekly weekend shift (Req: schedule 5.2 visual distinction)
            var weekendWardA = new Shift { Name = "Weekend Day", Location = "Ward A", StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(16, 0, 0), Frequency = Frequency.Weekly, Interval = 2, DaysOfWeek = "Saturday,Sunday" };
            // Weekly Tue/Thu shift
            var morningWardB = new Shift { Name = "Morning Shift", Location = "Ward B", StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(15, 0, 0), Frequency = Frequency.Weekly, Interval = 1, DaysOfWeek = "Tuesday,Thursday" };
            // Overlapping shift for overlap detection testing (Req: schedule 2.1-2.3)
            var midDayWardA = new Shift { Name = "Mid-Day Shift", Location = "Ward A", StartTime = new TimeSpan(11, 0, 0), EndTime = new TimeSpan(19, 0, 0), Frequency = Frequency.Weekly, Interval = 1, DaysOfWeek = "Monday,Wednesday,Friday" };

            context.Shifts.AddRange(morningWardA, eveningWardA, nightWardB, weekendWardA, morningWardB, midDayWardA);
            await context.SaveChangesAsync();

            // ── Shift Assignments ──
            employees[0].Shifts.Add(morningWardA);  // Alice - weekday mornings
            employees[1].Shifts.Add(eveningWardA);  // Bob - weekday evenings
            employees[2].Shifts.Add(morningWardB);  // Carol - Tue/Thu mornings
            employees[3].Shifts.Add(weekendWardA);  // David - bi-weekly weekends
            employees[4].Shifts.Add(morningWardA);  // Eva - weekday mornings
            employees[5].Shifts.Add(nightWardB);    // Frank (inactive) - should be filtered out
            // Carol also on mid-day for overlap scenario testing
            employees[2].Shifts.Add(midDayWardA);
            await context.SaveChangesAsync();

            // ── Clients ──
            var clients = new List<Client>
            {
                new() { Name = "Margaret Brown", Address = "Vancouver", DateOfBirth = new DateTime(1940, 6, 20), Balance = 1500.00m },
                new() { Name = "Robert Taylor", Address = "Burnaby", DateOfBirth = new DateTime(1935, 2, 14), Balance = 2200.00m },
                new() { Name = "Helen White", Address = "Surrey", DateOfBirth = new DateTime(1942, 10, 5), Balance = 800.00m },
            };
            context.Clients.AddRange(clients);
            await context.SaveChangesAsync();

            // ── Services ──
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

            // ── Attendance Records ──
            // Covers all AdjustmentTypes: None, Sick, Vacation, NoShow, Late
            // Covers: auto-creation defaults (Req: attendance 5.1), HoursToPay calc (Req: attendance 9.1),
            //         late arrival clock-in (Req: attendance 3.3), no-show zero pay (Req: attendance 4.1),
            //         vacation conflict for schedule assignment (Req: schedule 3.1-3.2)
            var today = DateTime.Today;
            var periodStart = today.AddDays(-13);
            var records = new List<AttendanceRecord>();

            // Alice (Morning Ward A, weekdays) - has Sick, Late, and normal days
            for (var d = periodStart; d <= today; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var adj = AdjustmentType.None;
                decimal hours = 8.00m;
                DateTime? clockIn = d.Add(new TimeSpan(7, 0, 0));
                DateTime? clockOut = d.Add(new TimeSpan(15, 0, 0));

                // Sick day (Req: attendance 2.3 - full shift duration retained)
                if (d == periodStart.AddDays(2))
                {
                    adj = AdjustmentType.Sick;
                    clockIn = null;
                    clockOut = null;
                }
                // Late arrival (Req: attendance 3.3 - HoursToPay = EndTime - ClockInTime)
                if (d == periodStart.AddDays(7))
                {
                    adj = AdjustmentType.Late;
                    hours = 7.50m;
                    clockIn = d.Add(new TimeSpan(7, 30, 0));
                }

                records.Add(new AttendanceRecord { EmployeeId = employees[0].ID, ShiftId = morningWardA.Id, TargetDate = d, ClockInTime = clockIn, ClockOutTime = clockOut, HoursToPay = hours, AdjustmentType = adj });
            }

            // Bob (Evening Ward A, weekdays) - has NoShow
            for (var d = periodStart; d <= today; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var adj = AdjustmentType.None;
                decimal hours = 8.00m;
                DateTime? clockIn = d.Add(new TimeSpan(15, 0, 0));
                DateTime? clockOut = d.Add(new TimeSpan(23, 0, 0));

                // No-show (Req: attendance 4.1 - HoursToPay = 0, clock times null)
                if (d == periodStart.AddDays(4))
                {
                    adj = AdjustmentType.NoShow;
                    hours = 0.00m;
                    clockIn = null;
                    clockOut = null;
                }

                records.Add(new AttendanceRecord { EmployeeId = employees[1].ID, ShiftId = eveningWardA.Id, TargetDate = d, ClockInTime = clockIn, ClockOutTime = clockOut, HoursToPay = hours, AdjustmentType = adj });
            }

            // Eva - upcoming vacation (Req: attendance 2.4, schedule 3.1 vacation conflict)
            for (var i = 1; i <= 5; i++)
            {
                var vacDate = today.AddDays(i);
                if (vacDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                records.Add(new AttendanceRecord
                {
                    EmployeeId = employees[4].ID,
                    ShiftId = morningWardA.Id,
                    TargetDate = vacDate,
                    HoursToPay = 8.00m,
                    AdjustmentType = AdjustmentType.Vacation,
                    Notes = "Approved vacation"
                });
            }

            // Carol (Morning Ward B, Tue/Thu) - normal attendance
            for (var d = periodStart; d <= today; d = d.AddDays(1))
            {
                if (d.DayOfWeek is not (DayOfWeek.Tuesday or DayOfWeek.Thursday)) continue;
                records.Add(new AttendanceRecord
                {
                    EmployeeId = employees[2].ID,
                    ShiftId = morningWardB.Id,
                    TargetDate = d,
                    ClockInTime = d.Add(new TimeSpan(7, 0, 0)),
                    ClockOutTime = d.Add(new TimeSpan(15, 0, 0)),
                    HoursToPay = 8.00m,
                    AdjustmentType = AdjustmentType.None
                });
            }

            context.AttendanceRecords.AddRange(records);
            await context.SaveChangesAsync();

            // ── Audit Log Entries ──
            // Seed audit history so the AuditHistory view has data (Req: attendance 6.4)
            var hrUser = await CreateUserWithRole(userManager, "hr@orgmgmt.local", "Staff123!", "HR");
            var aliceSickRecord = records.First(r => r.EmployeeId == employees[0].ID && r.AdjustmentType == AdjustmentType.Sick);
            var bobNoShowRecord = records.First(r => r.EmployeeId == employees[1].ID && r.AdjustmentType == AdjustmentType.NoShow);

            var auditEntries = new List<AuditLogEntry>
            {
                // Alice: None → Sick
                new()
                {
                    AttendanceRecordId = aliceSickRecord.Id,
                    UserId = hrUser?.Id ?? "seeded-hr-user",
                    Timestamp = aliceSickRecord.TargetDate.AddHours(9),
                    PreviousAdjustmentType = AdjustmentType.None,
                    NewAdjustmentType = AdjustmentType.Sick,
                    PreviousHoursToPay = 8.00m,
                    NewHoursToPay = 8.00m
                },
                // Bob: None → NoShow
                new()
                {
                    AttendanceRecordId = bobNoShowRecord.Id,
                    UserId = hrUser?.Id ?? "seeded-hr-user",
                    Timestamp = bobNoShowRecord.TargetDate.AddHours(16),
                    PreviousAdjustmentType = AdjustmentType.None,
                    NewAdjustmentType = AdjustmentType.NoShow,
                    PreviousHoursToPay = 8.00m,
                    NewHoursToPay = 0.00m
                },
            };
            context.AuditLogEntries.AddRange(auditEntries);
            await context.SaveChangesAsync();

            // ── User Accounts ──
            // One account per role so every role can be tested (Req: auth 5.1, 6.1-6.5, 7.1-7.4)
            // hr@orgmgmt.local already created above
            await CreateUserWithRole(userManager, "payroll@orgmgmt.local", "Staff123!", "Payroll");
            await CreateUserWithRole(userManager, "scheduler@orgmgmt.local", "Staff123!", "ScheduleManager");
            await CreateUserWithRole(userManager, "manager@orgmgmt.local", "Staff123!", "DirectManager");
            await CreateUserWithRole(userManager, "employee@orgmgmt.local", "Staff123!", "Employee");
        }

        private static async Task<ApplicationUser?> CreateUserWithRole(UserManager<ApplicationUser> userManager, string email, string password, string role)
        {
            if (await userManager.FindByEmailAsync(email) is { } existing)
                return existing;
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, role);
                return user;
            }
            return null;
        }
    }
}
