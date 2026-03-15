namespace OrgMgmt.ViewModels
{
    public class EmployeeScheduleViewModel
    {
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeRole { get; set; } = string.Empty;
        public List<ShiftAssignmentItem> CurrentShifts { get; set; } = new();
        public List<ShiftOption> AvailableShifts { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }
    }
}
