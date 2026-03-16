namespace OrgMgmt.ViewModels
{
    public class PayrollRowViewModel
    {
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public decimal HourlyPayRate { get; set; }
        public decimal RegularHours { get; set; }
        public decimal SickHours { get; set; }
        public decimal VacationHours { get; set; }
        public decimal LateHours { get; set; }
        public int NoShowCount { get; set; }
        public decimal TotalHoursToPay { get; set; }
        public decimal GrossPay { get; set; }
    }
}