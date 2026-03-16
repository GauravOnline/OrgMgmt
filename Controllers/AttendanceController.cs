using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrgMgmt.Services;
using OrgMgmt.ViewModels;

namespace OrgMgmt.Controllers
{
    [Authorize(Roles = "Admin,HR,Payroll")]
    public class AttendanceController : Controller
    {
        private readonly AttendanceService _attendanceService;

        public AttendanceController(AttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
        }

        // GET: /Attendance?date=2025-01-15
        public async Task<IActionResult> Dashboard(DateTime? date)
        {
            var model = await _attendanceService.GetDashboardAsync(date);
            model.ErrorMessage = TempData["Error"] as string;
            model.SuccessMessage = TempData["Success"] as string;
            return View(model);
        }

        // POST: /Attendance/SaveAdjustment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAdjustment(AdjustmentFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Invalid form submission.";
                return RedirectToAction(nameof(Dashboard), new { date = model.SelectedDate.ToString("yyyy-MM-dd") });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var result = await _attendanceService.ApplyAdjustmentAsync(
                model.AttendanceRecordId,
                model.AdjustmentType,
                model.ClockInTime,
                model.Notes,
                userId!);

            if (result.Success)
            {
                TempData["Success"] = "Adjustment saved successfully.";
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
            }

            return RedirectToAction(nameof(Dashboard), new { date = model.SelectedDate.ToString("yyyy-MM-dd") });
        }

        // GET: /Attendance/AuditHistory/{attendanceRecordId}
        public async Task<IActionResult> AuditHistory(Guid attendanceRecordId)
        {
            var history = await _attendanceService.GetAuditHistoryAsync(attendanceRecordId);
            return View(history);
        }
    }
}
