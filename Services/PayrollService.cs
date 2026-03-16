using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;
using OrgMgmt.ViewModels;

namespace OrgMgmt.Services
{
    public class PayrollService
    {
        private readonly OrgDbContext _context;

        public PayrollService(OrgDbContext context)
        {
            _context = context;
        }

        public async Task<List<PayrollRowViewModel>> BuildReportAsync(DateTime startDate, DateTime endDate)
        {
            var employees = await _context.Employees
                .Where(e => e.IsActive)
                .OrderBy(e => e.Name)
                .ToListAsync();

            var records = await _context.AttendanceRecords
                .Where(a => a.TargetDate.Date >= startDate.Date && a.TargetDate.Date <= endDate.Date)
                .ToListAsync();

            return employees.Select(e =>
            {
                var emp = records.Where(a => a.EmployeeId == e.ID).ToList();

                var regular   = emp.Where(a => a.AdjustmentType == AdjustmentType.None).Sum(a => a.HoursToPay);
                var sick      = emp.Where(a => a.AdjustmentType == AdjustmentType.Sick).Sum(a => a.HoursToPay);
                var vacation  = emp.Where(a => a.AdjustmentType == AdjustmentType.Vacation).Sum(a => a.HoursToPay);
                var late      = emp.Where(a => a.AdjustmentType == AdjustmentType.Late).Sum(a => a.HoursToPay);
                var noShows   = emp.Count(a => a.AdjustmentType == AdjustmentType.NoShow);
                var total     = emp.Sum(a => a.HoursToPay);

                return new PayrollRowViewModel
                {
                    EmployeeId     = e.ID,
                    EmployeeName   = e.Name,
                    Role           = e.Role,
                    HourlyPayRate  = e.HourlyPayRate,
                    RegularHours   = regular,
                    SickHours      = sick,
                    VacationHours  = vacation,
                    LateHours      = late,
                    NoShowCount    = noShows,
                    TotalHoursToPay = total,
                    GrossPay       = total * e.HourlyPayRate
                };
            }).ToList();
        }
    }
}
