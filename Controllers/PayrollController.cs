using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrgMgmt.Services;
using OrgMgmt.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OrgMgmt.Controllers
{
    [Authorize(Roles = "Admin,Payroll")]
    public class PayrollController : Controller
    {
        private readonly PayrollService _payrollService;

        public PayrollController(PayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        [HttpGet]
        public IActionResult Index() => View(new PayrollReportViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(PayrollReportViewModel model)
        {
            if (model.PeriodEndDate < model.PeriodStartDate)
            {
                ModelState.AddModelError("", "End date must be after start date.");
                return View(model);
            }

            model.Rows = await _payrollService.BuildReportAsync(model.PeriodStartDate, model.PeriodEndDate);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(DateTime periodStartDate, DateTime periodEndDate)
        {
            var rows = await _payrollService.BuildReportAsync(periodStartDate, periodEndDate);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Payroll");

            string[] headers = ["Employee", "Role", "Rate/hr", "Regular Hrs", "Sick Hrs", "Vacation Hrs", "Late Hrs", "No-Shows", "Total Hrs", "Gross Pay"];
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; int row = i + 2;
                ws.Cell(row, 1).Value = r.EmployeeName;
                ws.Cell(row, 2).Value = r.Role;
                ws.Cell(row, 3).Value = r.HourlyPayRate;
                ws.Cell(row, 4).Value = r.RegularHours;
                ws.Cell(row, 5).Value = r.SickHours;
                ws.Cell(row, 6).Value = r.VacationHours;
                ws.Cell(row, 7).Value = r.LateHours;
                ws.Cell(row, 8).Value = r.NoShowCount;
                ws.Cell(row, 9).Value = r.TotalHoursToPay;
                ws.Cell(row, 10).Value = r.GrossPay;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"payroll-{periodStartDate:yyyyMMdd}-{periodEndDate:yyyyMMdd}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> ExportPdf(DateTime periodStartDate, DateTime periodEndDate)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var rows = await _payrollService.BuildReportAsync(periodStartDate, periodEndDate);

            var pdf = Document.Create(c => c.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4.Landscape());
                page.Header()
                    .Text($"Payroll Report: {periodStartDate:yyyy-MM-dd} to {periodEndDate:yyyy-MM-dd}")
                    .FontSize(14).SemiBold();

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(1);
                    });

                    table.Header(h =>
                    {
                        foreach (var col in new[] { "Employee", "Role", "Rate", "Regular", "Sick", "Vacation", "Late", "No-Shows", "Total Hrs", "Gross Pay" })
                            h.Cell().Text(col).SemiBold();
                    });

                    foreach (var r in rows)
                    {
                        table.Cell().Text(r.EmployeeName);
                        table.Cell().Text(r.Role);
                        table.Cell().Text(r.HourlyPayRate.ToString("C"));
                        table.Cell().Text(r.RegularHours.ToString("0.##"));
                        table.Cell().Text(r.SickHours.ToString("0.##"));
                        table.Cell().Text(r.VacationHours.ToString("0.##"));
                        table.Cell().Text(r.LateHours.ToString("0.##"));
                        table.Cell().Text(r.NoShowCount.ToString());
                        table.Cell().Text(r.TotalHoursToPay.ToString("0.##"));
                        table.Cell().Text(r.GrossPay.ToString("C"));
                    }
                });
            })).GeneratePdf();

            return File(pdf, "application/pdf",
                $"payroll-{periodStartDate:yyyyMMdd}-{periodEndDate:yyyyMMdd}.pdf");
        }
    }
}
