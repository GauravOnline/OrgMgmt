using OrgMgmt.Models;

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
