using OrgMgmt.Models;

namespace OrgMgmt.ViewModels
{
    public class AttendanceRowViewModel
    {
        public Guid AttendanceRecordId { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string ShiftName { get; set; } = string.Empty;
        public TimeSpan ShiftStartTime { get; set; }
        public TimeSpan ShiftEndTime { get; set; }
        public string ShiftLocation { get; set; } = string.Empty;
        public AdjustmentType AdjustmentType { get; set; }
        public decimal HoursToPay { get; set; }
        public DateTime? ClockInTime { get; set; }
        public string? Notes { get; set; }
    }
}
