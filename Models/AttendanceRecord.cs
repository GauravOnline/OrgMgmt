using System.ComponentModel.DataAnnotations;

namespace OrgMgmt.Models
{
    public enum AdjustmentType
    {
        None,
        Sick,
        Vacation,
        NoShow,
        Late
    }

    public class AttendanceRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid EmployeeId { get; set; }
        public Employee Employee { get; set; } = null!;

        [Required]
        public Guid ShiftId { get; set; }
        public Shift Shift { get; set; } = null!;

        [Required]
        public DateTime TargetDate { get; set; }

        public DateTime? ClockInTime { get; set; }
        public DateTime? ClockOutTime { get; set; }

        public decimal HoursToPay { get; set; }

        public AdjustmentType AdjustmentType { get; set; } = AdjustmentType.None;

        public string? Notes { get; set; }
    }
}
