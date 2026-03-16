using System.ComponentModel.DataAnnotations;
using OrgMgmt.Models;

namespace OrgMgmt.ViewModels
{
    public class AdjustmentFormViewModel
    {
        [Required]
        public Guid AttendanceRecordId { get; set; }

        [Required]
        public AdjustmentType AdjustmentType { get; set; }

        public DateTime? ClockInTime { get; set; }

        public string? Notes { get; set; }

        public DateTime SelectedDate { get; set; }
    }
}
