using System.ComponentModel.DataAnnotations;

namespace OrgMgmt.ViewModels
{
    public class PayrollReportViewModel
    {
        [Required]
        [DataType(DataType.Date)]
        public DateTime PeriodStartDate { get; set; } = DateTime.Today.AddDays(-13);

        [Required]
        [DataType(DataType.Date)]
        public DateTime PeriodEndDate { get; set; } = DateTime.Today;

        public List<PayrollRowViewModel> Rows { get; set; } = new();
    }
}