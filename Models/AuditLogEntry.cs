using System.ComponentModel.DataAnnotations;

namespace OrgMgmt.Models
{
    public class AuditLogEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid AttendanceRecordId { get; set; }
        public AttendanceRecord AttendanceRecord { get; set; } = null!;

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public DateTime Timestamp { get; set; }

        public AdjustmentType PreviousAdjustmentType { get; set; }
        public AdjustmentType NewAdjustmentType { get; set; }

        public decimal PreviousHoursToPay { get; set; }
        public decimal NewHoursToPay { get; set; }
    }
}
