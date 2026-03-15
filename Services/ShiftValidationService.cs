using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;

namespace OrgMgmt.Services
{
    public class ShiftValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string? ConflictingShiftName { get; set; }
        public string? ConflictingDays { get; set; }
        public DateTime? ConflictingVacationDate { get; set; }
    }

    public class ShiftValidationService
    {
        public async Task<ShiftValidationResult> ValidateOverlap(
            Guid employeeId, Guid shiftId, OrgDbContext context)
        {
            var proposedShift = await context.Shifts.FindAsync(shiftId);
            if (proposedShift == null)
                return new ShiftValidationResult { IsValid = false, ErrorMessage = "Shift not found." };

            var proposedDays = ParseDays(proposedShift.DaysOfWeek);

            var existingShifts = await context.Employees
                .Where(e => e.ID == employeeId)
                .SelectMany(e => e.Shifts)
                .ToListAsync();

            foreach (var existing in existingShifts)
            {
                var existingDays = ParseDays(existing.DaysOfWeek);
                var commonDays = proposedDays.Intersect(existingDays).ToList();

                if (commonDays.Count > 0
                    && existing.StartTime < proposedShift.EndTime
                    && proposedShift.StartTime < existing.EndTime)
                {
                    return new ShiftValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Overlaps with '{existing.Name}' on {string.Join(", ", commonDays)} " +
                                       $"({existing.StartTime:hh\\:mm} - {existing.EndTime:hh\\:mm}).",
                        ConflictingShiftName = existing.Name,
                        ConflictingDays = string.Join(", ", commonDays)
                    };
                }
            }

            return new ShiftValidationResult { IsValid = true };
        }

        public async Task<ShiftValidationResult> ValidateVacationConflict(
            Guid employeeId, Guid shiftId, OrgDbContext context)
        {
            var proposedShift = await context.Shifts.FindAsync(shiftId);
            if (proposedShift == null)
                return new ShiftValidationResult { IsValid = false, ErrorMessage = "Shift not found." };

            var proposedDays = ParseDays(proposedShift.DaysOfWeek);
            if (proposedDays.Count == 0)
                return new ShiftValidationResult { IsValid = true };

            var recurrenceWindowDays = proposedShift.Interval == 2 ? 14 : 7;
            var today = DateTime.Today;
            var windowEnd = today.AddDays(recurrenceWindowDays);

            var vacationRecords = await context.AttendanceRecords
                .Where(a => a.EmployeeId == employeeId && a.AdjustmentType == AdjustmentType.Vacation)
                .Where(a => a.TargetDate >= today && a.TargetDate < windowEnd)
                .ToListAsync();

            foreach (var vacation in vacationRecords)
            {
                var vacationDayName = vacation.TargetDate.DayOfWeek.ToString();
                if (proposedDays.Contains(vacationDayName))
                {
                    return new ShiftValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Conflicts with vacation on {vacation.TargetDate:yyyy-MM-dd} ({vacationDayName}).",
                        ConflictingVacationDate = vacation.TargetDate
                    };
                }
            }

            return new ShiftValidationResult { IsValid = true };
        }

        private static HashSet<string> ParseDays(string? daysOfWeek)
        {
            if (string.IsNullOrWhiteSpace(daysOfWeek))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return new HashSet<string>(
                daysOfWeek.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
