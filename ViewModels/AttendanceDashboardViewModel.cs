namespace OrgMgmt.ViewModels
{
    public class AttendanceDashboardViewModel
    {
        public DateTime SelectedDate { get; set; }
        public List<AttendanceRowViewModel> Rows { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }
    }
}
