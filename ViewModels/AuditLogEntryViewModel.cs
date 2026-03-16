using OrgMgmt.Models;

namespace OrgMgmt.ViewModels
{
    public class AuditLogEntryViewModel
    {
        public DateTime Timestamp { get; set; }
        public string UserName { get; set; } = string.Empty;
        public AdjustmentType PreviousAdjustmentType { get; set; }
        public AdjustmentType NewAdjustmentType { get; set; }
        public decimal PreviousHoursToPay { get; set; }
        public decimal NewHoursToPay { get; set; }
    }
}
