using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;
using OrgMgmt.ViewModels;

namespace OrgMgmt.Services
{
    public class AdjustmentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class AttendanceService
    {
        private readonly OrgDbContext _context;

        public AttendanceService(OrgDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Determines if a shift is scheduled on a given date based on DaysOfWeek and recurrence interval.
        /// </summary>
        public static bool IsShiftScheduledOnDate(Shift shift, DateTime date)
        {
            var scheduledDays = ParseDays(shift.DaysOfWeek);
            if (scheduledDays.Count == 0)
                return false;

            var dayName = date.DayOfWeek.ToString();
            if (!scheduledDays.Contains(dayName))
                return false;

            // For bi-weekly (Interval == 2), check if the date falls in an active week
            if (shift.Interval == 2)
            {
                // Use a fixed reference start date (a known Monday)
                var referenceDate = new DateTime(2025, 1, 6); // Monday, Jan 6, 2025
                var daysDiff = (date.Date - referenceDate).Days;
                var weeksDiff = daysDiff / 7;

                // Shift is scheduled on even weeks (0, 2, 4, ...) relative to reference
                if (weeksDiff % 2 != 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Pure function that calculates HoursToPay based on adjustment type, shift times, and optional clock-in time.
        /// </summary>
        public static decimal CalculateHoursToPay(
            AdjustmentType adjustmentType,
            TimeSpan shiftStartTime,
            TimeSpan shiftEndTime,
            DateTime? clockInTime)
        {
            switch (adjustmentType)
            {
                case AdjustmentType.None:
                case AdjustmentType.Sick:
                case AdjustmentType.Vacation:
                    return Math.Round((decimal)(shiftEndTime - shiftStartTime).TotalHours, 2, MidpointRounding.AwayFromZero);

                case AdjustmentType.NoShow:
                    return 0.00m;

                case AdjustmentType.Late:
                    return Math.Round((decimal)(shiftEndTime - clockInTime!.Value.TimeOfDay).TotalHours, 2, MidpointRounding.AwayFromZero);

                default:
                    return Math.Round((decimal)(shiftEndTime - shiftStartTime).TotalHours, 2, MidpointRounding.AwayFromZero);
            }
        }

        /// <summary>
        /// Returns dashboard data for a given date, auto-creating missing attendance records.
        /// </summary>
        public async Task<AttendanceDashboardViewModel> GetDashboardAsync(DateTime? date)
        {
            var selectedDate = date ?? DateTime.Today;

            var employees = await _context.Employees
                .Include(e => e.Shifts)
                .Where(e => e.IsActive)
                .ToListAsync();

            var rows = new List<AttendanceRowViewModel>();

            foreach (var employee in employees)
            {
                foreach (var shift in employee.Shifts)
                {
                    if (!IsShiftScheduledOnDate(shift, selectedDate))
                        continue;

                    // Check for existing attendance record
                    var record = await _context.AttendanceRecords
                        .FirstOrDefaultAsync(a =>
                            a.EmployeeId == employee.ID &&
                            a.ShiftId == shift.Id &&
                            a.TargetDate == selectedDate.Date);

                    // Auto-create missing record with defaults
                    if (record == null)
                    {
                        record = new AttendanceRecord
                        {
                            EmployeeId = employee.ID,
                            ShiftId = shift.Id,
                            TargetDate = selectedDate.Date,
                            AdjustmentType = AdjustmentType.None,
                            HoursToPay = CalculateHoursToPay(AdjustmentType.None, shift.StartTime, shift.EndTime, null)
                        };
                        _context.AttendanceRecords.Add(record);
                    }

                    rows.Add(new AttendanceRowViewModel
                    {
                        AttendanceRecordId = record.Id,
                        EmployeeId = employee.ID,
                        EmployeeName = employee.Name,
                        ShiftName = shift.Name,
                        ShiftStartTime = shift.StartTime,
                        ShiftEndTime = shift.EndTime,
                        ShiftLocation = shift.Location,
                        AdjustmentType = record.AdjustmentType,
                        HoursToPay = record.HoursToPay,
                        ClockInTime = record.ClockInTime,
                        Notes = record.Notes
                    });
                }
            }

            if (rows.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            var viewModel = new AttendanceDashboardViewModel
            {
                SelectedDate = selectedDate,
                Rows = rows
            };

            if (rows.Count == 0)
            {
                viewModel.ErrorMessage = "No scheduled employees for this date.";
            }

            return viewModel;
        }

        /// <summary>
        /// Applies an adjustment to an attendance record. Returns a result with success/error info.
        /// </summary>
        public async Task<AdjustmentResult> ApplyAdjustmentAsync(
            Guid attendanceRecordId,
            AdjustmentType adjustmentType,
            DateTime? clockInTime,
            string? notes,
            string userId)
        {
            var record = await _context.AttendanceRecords
                .Include(a => a.Shift)
                .FirstOrDefaultAsync(a => a.Id == attendanceRecordId);

            if (record == null)
            {
                return new AdjustmentResult
                {
                    Success = false,
                    ErrorMessage = "Attendance record not found."
                };
            }

            // Validate Late adjustment requires ClockInTime within shift window
            if (adjustmentType == AdjustmentType.Late)
            {
                if (clockInTime == null)
                {
                    return new AdjustmentResult
                    {
                        Success = false,
                        ErrorMessage = "Clock-in time is required for Late Arrival adjustment."
                    };
                }

                var clockInTimeOfDay = clockInTime.Value.TimeOfDay;
                if (clockInTimeOfDay < record.Shift.StartTime || clockInTimeOfDay > record.Shift.EndTime)
                {
                    return new AdjustmentResult
                    {
                        Success = false,
                        ErrorMessage = "Clock-in time must be within the shift start and end times."
                    };
                }
            }

            // Capture previous values for audit
            var previousAdjustmentType = record.AdjustmentType;
            var previousHoursToPay = record.HoursToPay;

            // Calculate new HoursToPay
            var newHoursToPay = CalculateHoursToPay(
                adjustmentType,
                record.Shift.StartTime,
                record.Shift.EndTime,
                clockInTime);

            // Update the record
            record.AdjustmentType = adjustmentType;
            record.HoursToPay = newHoursToPay;
            record.Notes = notes;

            if (adjustmentType == AdjustmentType.NoShow)
            {
                record.ClockInTime = null;
                record.ClockOutTime = null;
            }
            else if (adjustmentType == AdjustmentType.Late)
            {
                record.ClockInTime = clockInTime;
            }

            // Create audit log entry
            var auditEntry = new AuditLogEntry
            {
                AttendanceRecordId = record.Id,
                UserId = userId,
                Timestamp = DateTime.UtcNow,
                PreviousAdjustmentType = previousAdjustmentType,
                NewAdjustmentType = adjustmentType,
                PreviousHoursToPay = previousHoursToPay,
                NewHoursToPay = newHoursToPay
            };

            _context.AuditLogEntries.Add(auditEntry);

            try
            {
                await _context.SaveChangesAsync();
                return new AdjustmentResult { Success = true };
            }
            catch (DbUpdateException)
            {
                return new AdjustmentResult
                {
                    Success = false,
                    ErrorMessage = "A database error occurred while saving the adjustment."
                };
            }
        }

        /// <summary>
        /// Returns audit history for a specific attendance record.
        /// </summary>
        public async Task<List<AuditLogEntryViewModel>> GetAuditHistoryAsync(Guid attendanceRecordId)
        {
            var entries = await _context.AuditLogEntries
                .Where(e => e.AttendanceRecordId == attendanceRecordId)
                .OrderBy(e => e.Timestamp)
                .Select(e => new AuditLogEntryViewModel
                {
                    Timestamp = e.Timestamp,
                    UserName = e.UserId,
                    PreviousAdjustmentType = e.PreviousAdjustmentType,
                    NewAdjustmentType = e.NewAdjustmentType,
                    PreviousHoursToPay = e.PreviousHoursToPay,
                    NewHoursToPay = e.NewHoursToPay
                })
                .ToListAsync();

            return entries;
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
