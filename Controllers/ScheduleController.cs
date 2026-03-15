using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;
using OrgMgmt.Services;
using OrgMgmt.ViewModels;

namespace OrgMgmt.Controllers
{
    [Authorize(Roles = "ScheduleManager,Admin")]
    public class ScheduleController : Controller
    {
        private readonly OrgDbContext _context;
        private readonly ShiftValidationService _validationService;

        public ScheduleController(OrgDbContext context, ShiftValidationService validationService)
        {
            _context = context;
            _validationService = validationService;
        }

        // GET: /Schedule/Assign
        public async Task<IActionResult> Assign()
        {
            var model = new EmployeeListViewModel
            {
                Employees = await _context.Employees
                    .Where(e => e.IsActive)
                    .Select(e => new EmployeeListItem
                    {
                        Id = e.ID,
                        Name = e.Name,
                        Role = e.Role
                    })
                    .ToListAsync()
            };
            return View(model);
        }

        // GET: /Schedule/AssignToEmployee/{id}
        public async Task<IActionResult> AssignToEmployee(Guid id)
        {
            var employee = await _context.Employees
                .Include(e => e.Shifts)
                .FirstOrDefaultAsync(e => e.ID == id);

            if (employee == null)
                return NotFound();

            if (!employee.IsActive)
                return RedirectToAction(nameof(Assign));

            var allShifts = await _context.Shifts.ToListAsync();
            var assignedShiftIds = employee.Shifts.Select(s => s.Id).ToHashSet();

            var model = new EmployeeScheduleViewModel
            {
                EmployeeId = employee.ID,
                EmployeeName = employee.Name,
                EmployeeRole = employee.Role,
                CurrentShifts = employee.Shifts.Select(s => new ShiftAssignmentItem
                {
                    ShiftId = s.Id,
                    Name = s.Name,
                    Location = s.Location,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    DaysOfWeek = s.DaysOfWeek ?? string.Empty,
                    Interval = s.Interval
                }).ToList(),
                AvailableShifts = allShifts
                    .Where(s => !assignedShiftIds.Contains(s.Id))
                    .Select(s => new ShiftOption
                    {
                        ShiftId = s.Id,
                        Name = s.Name,
                        Location = s.Location,
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        DaysOfWeek = s.DaysOfWeek ?? string.Empty,
                        Interval = s.Interval
                    }).ToList(),
                ErrorMessage = TempData["Error"] as string,
                SuccessMessage = TempData["Success"] as string
            };

            return View(model);
        }

        // POST: /Schedule/Assign
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(Guid employeeId, Guid shiftId)
        {
            // Validate overlap
            var overlapResult = await _validationService.ValidateOverlap(employeeId, shiftId, _context);
            if (!overlapResult.IsValid)
            {
                TempData["Error"] = overlapResult.ErrorMessage;
                return RedirectToAction(nameof(AssignToEmployee), new { id = employeeId });
            }

            // Validate vacation conflict
            var vacationResult = await _validationService.ValidateVacationConflict(employeeId, shiftId, _context);
            if (!vacationResult.IsValid)
            {
                TempData["Error"] = vacationResult.ErrorMessage;
                return RedirectToAction(nameof(AssignToEmployee), new { id = employeeId });
            }

            // Attempt to save the assignment
            try
            {
                var employee = await _context.Employees
                    .Include(e => e.Shifts)
                    .FirstOrDefaultAsync(e => e.ID == employeeId);

                if (employee == null)
                    return NotFound();

                var shift = await _context.Shifts.FindAsync(shiftId);
                if (shift == null)
                    return NotFound();

                employee.Shifts.Add(shift);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Successfully assigned '{shift.Name}' to {employee.Name}.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "This shift is already assigned to the employee.";
            }

            return RedirectToAction(nameof(AssignToEmployee), new { id = employeeId });
        }

        // POST: /Schedule/Remove
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(Guid employeeId, Guid shiftId)
        {
            var employee = await _context.Employees
                .Include(e => e.Shifts)
                .FirstOrDefaultAsync(e => e.ID == employeeId);

            if (employee == null)
                return NotFound();

            var shift = employee.Shifts.FirstOrDefault(s => s.Id == shiftId);
            if (shift != null)
            {
                employee.Shifts.Remove(shift);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(AssignToEmployee), new { id = employeeId });
        }
    }
}
