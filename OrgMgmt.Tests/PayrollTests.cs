using System.Globalization;
using System.Net;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrgMgmt.Models;
using OrgMgmt.Controllers;
using OrgMgmt.Services;
using OrgMgmt.Tests.TestInfrastructure;
using OrgMgmt.ViewModels;
using UglyToad.PdfPig;
using Xunit;

namespace OrgMgmt.Tests;

public class PayrollTests
{
    /// <summary>
    /// Verifies the default payroll period shown on the first page load.
    /// This protects the starting range payroll users see before generating a report.
    /// Expected result: the bi-weekly dates are prefilled and export links stay hidden.
    /// </summary>
    [Fact]
    public async Task Index_FirstLoad_UsesDefaultBiWeeklyPeriodAndHidesExportActions()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        await factory.LoginAsync(client, "payroll@orgmgmt.local", "Staff123!");

        var html = await client.GetStringAsync("/Payroll");

        html.Should().Contain($"value=\"{DateTime.Today.AddDays(-13):yyyy-MM-dd}\"");
        html.Should().Contain($"value=\"{DateTime.Today:yyyy-MM-dd}\"");
        html.Should().NotContain("Export Excel");
        html.Should().NotContain("Export PDF");
    }

    /// <summary>
    /// Confirms that the controller rejects a payroll range with the dates reversed.
    /// This blocks an invalid input case that would produce misleading report output.
    /// Expected result: the view is returned with the date-order validation error.
    /// </summary>
    [Fact]
    public async Task Index_EndDateEarlierThanStartDate_ReturnsViewWithModelError()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(Index_EndDateEarlierThanStartDate_ReturnsViewWithModelError));
        var controller = new PayrollController(new PayrollService(context));

        var model = new PayrollReportViewModel
        {
            PeriodStartDate = new DateTime(2025, 1, 20),
            PeriodEndDate = new DateTime(2025, 1, 6)
        };

        var result = await controller.Index(model);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.Model.Should().BeSameAs(model);
        controller.ModelState[string.Empty]!.Errors.Should().ContainSingle(error => error.ErrorMessage == "End date must be after start date.");
    }

    /// <summary>
    /// Ensures that valid custom payroll ranges are accepted by the current controller logic.
    /// This keeps the tests aligned with the implemented range behavior instead of assuming a strict 14-day rule.
    /// Expected result: each valid range returns the report view with the selected dates preserved.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(21)]
    public async Task Index_ValidCustomRange_ReturnsViewForImplementedRangeLengths(int days)
    {
        await using var context = TestDbHelpers.CreateInMemoryContext($"{nameof(Index_ValidCustomRange_ReturnsViewForImplementedRangeLengths)}-{days}");
        var employee = TestDbHelpers.CreateEmployee();
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var controller = new PayrollController(new PayrollService(context));
        var startDate = new DateTime(2025, 1, 1);
        var model = new PayrollReportViewModel
        {
            PeriodStartDate = startDate,
            PeriodEndDate = startDate.AddDays(days - 1)
        };

        var result = await controller.Index(model);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var returnedModel = viewResult.Model.Should().BeOfType<PayrollReportViewModel>().Subject;
        returnedModel.PeriodStartDate.Should().Be(startDate);
        returnedModel.PeriodEndDate.Should().Be(startDate.AddDays(days - 1));
        returnedModel.Rows.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that inactive employees are excluded from payroll output.
    /// This keeps payroll from surfacing or paying inactive staff.
    /// Expected result: only active employees appear in the generated rows.
    /// </summary>
    [Fact]
    public async Task BuildReport_InactiveEmployeeWithAttendance_IsExcludedFromPayrollRows()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(BuildReport_InactiveEmployeeWithAttendance_IsExcludedFromPayrollRows));
        var shift = TestDbHelpers.CreateShift();
        var activeEmployee = TestDbHelpers.CreateEmployee(name: "Active Employee");
        var inactiveEmployee = TestDbHelpers.CreateEmployee(name: "Inactive Employee", isActive: false);

        context.Shifts.Add(shift);
        context.Employees.AddRange(activeEmployee, inactiveEmployee);
        context.AttendanceRecords.AddRange(
            TestDbHelpers.CreateAttendanceRecord(activeEmployee, shift, new DateTime(2025, 1, 6), hoursToPay: 8.00m),
            TestDbHelpers.CreateAttendanceRecord(inactiveEmployee, shift, new DateTime(2025, 1, 6), hoursToPay: 8.00m));
        await context.SaveChangesAsync();

        var rows = await new PayrollService(context).BuildReportAsync(new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        rows.Should().ContainSingle(row => row.EmployeeName == "Active Employee");
        rows.Should().NotContain(row => row.EmployeeName == "Inactive Employee");
    }

    /// <summary>
    /// Confirms that the payroll range includes boundary dates and excludes outside dates.
    /// This protects the inclusive date filtering used to calculate totals.
    /// Expected result: only the start-date and end-date records affect totals.
    /// </summary>
    [Fact]
    public async Task BuildReport_BoundaryDatesIncludedAndOutsideDatesExcluded()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(BuildReport_BoundaryDatesIncludedAndOutsideDatesExcluded));
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift();
        var startDate = new DateTime(2025, 1, 1);
        var endDate = new DateTime(2025, 1, 14);

        context.Shifts.Add(shift);
        context.Employees.Add(employee);
        context.AttendanceRecords.AddRange(
            TestDbHelpers.CreateAttendanceRecord(employee, shift, startDate, hoursToPay: 8.00m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, endDate, hoursToPay: 8.00m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, startDate.AddDays(-1), hoursToPay: 5.00m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, endDate.AddDays(1), hoursToPay: 6.00m));
        await context.SaveChangesAsync();

        var row = (await new PayrollService(context).BuildReportAsync(startDate, endDate)).Single();

        row.RegularHours.Should().Be(16.00m);
        row.TotalHoursToPay.Should().Be(16.00m);
    }

    /// <summary>
    /// Verifies that each attendance adjustment type maps to the correct payroll columns.
    /// This keeps paid categories and no-show counts auditable in the report.
    /// Expected result: regular, sick, vacation, late, and no-show values stay separated.
    /// </summary>
    [Fact]
    public async Task BuildReport_MixedAdjustments_AreSeparatedIntoTheirCorrectColumns()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(BuildReport_MixedAdjustments_AreSeparatedIntoTheirCorrectColumns));
        var employee = TestDbHelpers.CreateEmployee();
        var shift = TestDbHelpers.CreateShift();

        context.Shifts.Add(shift);
        context.Employees.Add(employee);
        context.AttendanceRecords.AddRange(
            TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 1), AdjustmentType.None, 8.00m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 2), AdjustmentType.Sick, 8.00m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 3), AdjustmentType.Vacation, 8.00m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 4), AdjustmentType.Late, 7.50m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 5), AdjustmentType.NoShow, 0.00m));
        await context.SaveChangesAsync();

        var row = (await new PayrollService(context).BuildReportAsync(new DateTime(2025, 1, 1), new DateTime(2025, 1, 31))).Single();

        row.RegularHours.Should().Be(8.00m);
        row.SickHours.Should().Be(8.00m);
        row.VacationHours.Should().Be(8.00m);
        row.LateHours.Should().Be(7.50m);
        row.NoShowCount.Should().Be(1);
        row.TotalHoursToPay.Should().Be(31.50m);
    }

    /// <summary>
    /// Ensures that active employees without attendance still appear in the report.
    /// This guards the service behavior that returns zero rows instead of omitting active staff.
    /// Expected result: the employee row is present with zero totals.
    /// </summary>
    [Fact]
    public async Task BuildReport_ActiveEmployeeWithoutAttendance_ReturnsZeroTotalsRow()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(BuildReport_ActiveEmployeeWithoutAttendance_ReturnsZeroTotalsRow));
        context.Employees.Add(TestDbHelpers.CreateEmployee(name: "Zero Hours Employee"));
        await context.SaveChangesAsync();

        var row = (await new PayrollService(context).BuildReportAsync(new DateTime(2025, 1, 1), new DateTime(2025, 1, 31))).Single();

        row.EmployeeName.Should().Be("Zero Hours Employee");
        row.TotalHoursToPay.Should().Be(0.00m);
        row.GrossPay.Should().Be(0.00m);
        row.NoShowCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies that fractional paid hours are preserved through totals and gross pay.
    /// This avoids underpaying or overpaying when decimal hours are involved.
    /// Expected result: the report keeps the exact decimal hours and pay amount.
    /// </summary>
    [Fact]
    public async Task BuildReport_FractionalHours_TotalHoursAndGrossPay_AreCalculatedExactly()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(BuildReport_FractionalHours_TotalHoursAndGrossPay_AreCalculatedExactly));
        var employee = TestDbHelpers.CreateEmployee(hourlyPayRate: 21.75m);
        var shift = TestDbHelpers.CreateShift();

        context.Shifts.Add(shift);
        context.Employees.Add(employee);
        context.AttendanceRecords.AddRange(
            TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 1), AdjustmentType.None, 7.50m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, new DateTime(2025, 1, 2), AdjustmentType.Vacation, 8.00m));
        await context.SaveChangesAsync();

        var row = (await new PayrollService(context).BuildReportAsync(new DateTime(2025, 1, 1), new DateTime(2025, 1, 31))).Single();

        row.TotalHoursToPay.Should().Be(15.50m);
        row.GrossPay.Should().Be(337.1250m);
    }

    /// <summary>
    /// Confirms that payroll rows are ordered by employee name.
    /// This keeps the page and exports stable and easier to review.
    /// Expected result: the rows come back in alphabetical order.
    /// </summary>
    [Fact]
    public async Task BuildReport_MultipleEmployees_AreOrderedByName()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(BuildReport_MultipleEmployees_AreOrderedByName));
        var alpha = TestDbHelpers.CreateEmployee(name: "Alice Zebra");
        var beta = TestDbHelpers.CreateEmployee(name: "Bob Yellow");
        var gamma = TestDbHelpers.CreateEmployee(name: "Carol Adams");

        context.Employees.AddRange(alpha, beta, gamma);
        await context.SaveChangesAsync();

        var rows = await new PayrollService(context).BuildReportAsync(new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        rows.Select(row => row.EmployeeName).Should().ContainInOrder("Alice Zebra", "Bob Yellow", "Carol Adams");
    }

    /// <summary>
    /// Verifies that the HTML payroll page shows the generated totals and enables exports after generation.
    /// This checks that the rendered page matches backend calculations instead of changing them.
    /// Expected result: the rendered row matches the service output and the export links are visible.
    /// </summary>
    [Fact]
    public async Task PayrollPage_GeneratedHtml_RendersServiceTotalsAndShowsExportActions()
    {
        await using var factory = new TestWebApplicationFactory();
        var period = new PayrollPeriod(new DateTime(2025, 1, 1), new DateTime(2025, 1, 14));
        var expected = await SeedUniquePayrollScenarioAsync(factory, period);
        using var client = factory.CreateClient();

        await factory.LoginAsync(client, "payroll@orgmgmt.local", "Staff123!");

        var html = await PostPayrollFormAndReadHtmlAsync(factory, client, period);
        var employeeRowHtml = ExtractEmployeeRowHtml(html, expected.EmployeeName);

        html.Should().Contain(expected.EmployeeName);
        employeeRowHtml.Should().Contain($">{expected.RegularHours.ToString("0.##", CultureInfo.CurrentCulture)}<");
        employeeRowHtml.Should().Contain($">{expected.SickHours.ToString("0.##", CultureInfo.CurrentCulture)}<");
        employeeRowHtml.Should().Contain($">{expected.VacationHours.ToString("0.##", CultureInfo.CurrentCulture)}<");
        employeeRowHtml.Should().Contain($">{expected.LateHours.ToString("0.##", CultureInfo.CurrentCulture)}<");
        employeeRowHtml.Should().Contain($">{expected.TotalHoursToPay.ToString("0.##", CultureInfo.CurrentCulture)}<");
        employeeRowHtml.Should().Contain(expected.GrossPay.ToString("N2", CultureInfo.CurrentCulture));
        html.Should().Contain("Export Excel");
        html.Should().Contain("Export PDF");
    }

    /// <summary>
    /// Confirms that the Excel export returns the expected file metadata and tabular values.
    /// This protects both the download contract and the financial content inside the spreadsheet.
    /// Expected result: the worksheet contains the known payroll row and totals.
    /// </summary>
    [Fact]
    public async Task ExportExcel_KnownPayrollRows_ReturnsExpectedMetadataAndTabularContent()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(ExportExcel_KnownPayrollRows_ReturnsExpectedMetadataAndTabularContent));
        var period = new PayrollPeriod(new DateTime(2025, 1, 1), new DateTime(2025, 1, 14));
        var expected = await SeedPayrollScenarioAsync(context, period, "Excel Export Employee");
        var controller = new PayrollController(new PayrollService(context));

        var result = await controller.ExportExcel(period.StartDate, period.EndDate);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        fileResult.FileDownloadName.Should().Be($"payroll-{period.StartDate:yyyyMMdd}-{period.EndDate:yyyyMMdd}.xlsx");

        using var workbook = new XLWorkbook(new MemoryStream(fileResult.FileContents));
        var worksheet = workbook.Worksheet("Payroll");

        worksheet.Cell(1, 1).GetString().Should().Be("Employee");
        worksheet.Cell(1, 10).GetString().Should().Be("Gross Pay");
        worksheet.Cell(2, 1).GetString().Should().Be(expected.EmployeeName);
        worksheet.Cell(2, 4).GetValue<decimal>().Should().Be(expected.RegularHours);
        worksheet.Cell(2, 5).GetValue<decimal>().Should().Be(expected.SickHours);
        worksheet.Cell(2, 6).GetValue<decimal>().Should().Be(expected.VacationHours);
        worksheet.Cell(2, 7).GetValue<decimal>().Should().Be(expected.LateHours);
        worksheet.Cell(2, 8).GetValue<int>().Should().Be(expected.NoShowCount);
        worksheet.Cell(2, 9).GetValue<decimal>().Should().Be(expected.TotalHoursToPay);
        worksheet.Cell(2, 10).GetValue<decimal>().Should().Be(expected.GrossPay);
    }

    /// <summary>
    /// Verifies that the PDF export returns the expected file metadata and report content.
    /// This keeps the downloadable report aligned with the selected payroll period.
    /// Expected result: the PDF text includes the selected period and known payroll totals.
    /// </summary>
    [Fact]
    public async Task ExportPdf_KnownPayrollRows_ReturnsExpectedMetadataAndReportContent()
    {
        await using var context = TestDbHelpers.CreateInMemoryContext(nameof(ExportPdf_KnownPayrollRows_ReturnsExpectedMetadataAndReportContent));
        var period = new PayrollPeriod(new DateTime(2025, 1, 1), new DateTime(2025, 1, 14));
        var expected = await SeedPayrollScenarioAsync(context, period, "Pdf Export Employee");
        var controller = new PayrollController(new PayrollService(context));

        var result = await controller.ExportPdf(period.StartDate, period.EndDate);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("application/pdf");
        fileResult.FileDownloadName.Should().Be($"payroll-{period.StartDate:yyyyMMdd}-{period.EndDate:yyyyMMdd}.pdf");

        var pdfText = ReadPdfText(fileResult.FileContents);
        pdfText.Should().Contain($"Payroll Report: {period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}");
        pdfText.Should().Contain(expected.EmployeeName);
        pdfText.Should().Contain(expected.TotalHoursToPay.ToString("0.##", CultureInfo.CurrentCulture));
        pdfText.Should().Contain(expected.GrossPay.ToString("C", CultureInfo.CurrentCulture));
    }

    /// <summary>
    /// Ensures that the HTML page, Excel export, and PDF export stay consistent for one payroll range.
    /// This prevents reconciliation issues across the three output paths.
    /// Expected result: each output shows the same employee, totals, and date window.
    /// </summary>
    [Fact]
    public async Task PayrollOutputs_SameDateRange_StayConsistentAcrossHtmlExcelAndPdf()
    {
        await using var factory = new TestWebApplicationFactory();
        var period = new PayrollPeriod(new DateTime(2025, 1, 1), new DateTime(2025, 1, 14));
        var expected = await SeedUniquePayrollScenarioAsync(factory, period);
        using var client = factory.CreateClient();

        await factory.LoginAsync(client, "payroll@orgmgmt.local", "Staff123!");

        var html = await PostPayrollFormAndReadHtmlAsync(factory, client, period);
        using var excelResponse = await client.GetAsync($"/Payroll/ExportExcel?periodStartDate={period.StartDate:yyyy-MM-dd}&periodEndDate={period.EndDate:yyyy-MM-dd}");
        using var pdfResponse = await client.GetAsync($"/Payroll/ExportPdf?periodStartDate={period.StartDate:yyyy-MM-dd}&periodEndDate={period.EndDate:yyyy-MM-dd}");

        var excelBytes = await excelResponse.Content.ReadAsByteArrayAsync();
        var pdfBytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(excelBytes));
        var worksheet = workbook.Worksheet("Payroll");
        var pdfText = ReadPdfText(pdfBytes);
        var employeeRowNumber = FindWorksheetRowNumberByEmployeeName(worksheet, expected.EmployeeName);

        html.Should().Contain(expected.EmployeeName);
        html.Should().Contain(expected.TotalHoursToPay.ToString("0.##", CultureInfo.CurrentCulture));
        worksheet.Cell(employeeRowNumber, 1).GetString().Should().Be(expected.EmployeeName);
        worksheet.Cell(employeeRowNumber, 9).GetValue<decimal>().Should().Be(expected.TotalHoursToPay);
        pdfText.Should().Contain($"{period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}");
        pdfText.Should().Contain(expected.EmployeeName);
        pdfText.Should().Contain(expected.TotalHoursToPay.ToString("0.##", CultureInfo.CurrentCulture));
    }

    /// <summary>
    /// Confirms that payroll routes follow the current role-based authorization rules.
    /// This protects sensitive payroll data from unauthorized access.
    /// Expected result: Admin and Payroll are allowed, while others are redirected.
    /// </summary>
    [Theory]
    [InlineData(null, null, null, "/Account/Login")]
    [InlineData("admin@orgmgmt.local", "Admin123!", HttpStatusCode.OK, null)]
    [InlineData("payroll@orgmgmt.local", "Staff123!", HttpStatusCode.OK, null)]
    [InlineData("hr@orgmgmt.local", "Staff123!", HttpStatusCode.Redirect, "/Account/AccessDenied")]
    [InlineData("employee@orgmgmt.local", "Staff123!", HttpStatusCode.Redirect, "/Account/AccessDenied")]
    [InlineData("manager@orgmgmt.local", "Staff123!", HttpStatusCode.Redirect, "/Account/AccessDenied")]
    public async Task PayrollRoutes_RoleChecks_MatchTheCurrentAuthorizationRules(string? email, string? password, HttpStatusCode? expectedStatusCode, string? expectedRedirectPrefix)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateRedirectlessClient();

        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
        {
            await factory.LoginAsync(client, email, password);
        }

        using var response = await client.GetAsync("/Payroll");

        response.StatusCode.Should().Be(expectedStatusCode ?? HttpStatusCode.Redirect);

        if (!string.IsNullOrWhiteSpace(expectedRedirectPrefix))
        {
            response.Headers.Location.Should().NotBeNull();
            response.Headers.Location!.PathAndQuery.Should().StartWith(expectedRedirectPrefix);
        }
    }

    /// <summary>
    /// Verifies that payroll generation rejects posts without an anti-forgery token.
    /// This protects the report action from CSRF requests.
    /// Expected result: the request returns a bad-request response.
    /// </summary>
    [Fact]
    public async Task Index_PostWithoutAntiForgeryToken_IsRejected()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateRedirectlessClient();

        await factory.LoginAsync(client, "payroll@orgmgmt.local", "Staff123!");

        using var response = await client.PostAsync(
            "/Payroll",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["PeriodStartDate"] = "2025-01-01",
                ["PeriodEndDate"] = "2025-01-14"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<string> PostPayrollFormAndReadHtmlAsync(TestWebApplicationFactory factory, HttpClient client, PayrollPeriod period)
    {
        using var response = await factory.PostFormAsync(client, "/Payroll", new Dictionary<string, string?>
        {
            ["PeriodStartDate"] = period.StartDate.ToString("yyyy-MM-dd"),
            ["PeriodEndDate"] = period.EndDate.ToString("yyyy-MM-dd")
        });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string ReadPdfText(byte[] pdfBytes)
    {
        using var pdfDocument = PdfDocument.Open(new MemoryStream(pdfBytes));
        return string.Join(Environment.NewLine, pdfDocument.GetPages().Select(page => page.Text));
    }

    private static string ExtractEmployeeRowHtml(string html, string employeeName)
    {
        var marker = $"<td>{employeeName}</td>";
        var startIndex = html.IndexOf(marker, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0);

        var rowStart = html.LastIndexOf("<tr>", startIndex, StringComparison.Ordinal);
        var rowEnd = html.IndexOf("</tr>", startIndex, StringComparison.Ordinal);
        rowStart.Should().BeGreaterThanOrEqualTo(0);
        rowEnd.Should().BeGreaterThan(rowStart);

        return html.Substring(rowStart, rowEnd - rowStart);
    }

    private static int FindWorksheetRowNumberByEmployeeName(IXLWorksheet worksheet, string employeeName)
    {
        for (var rowNumber = 2; rowNumber <= worksheet.LastRowUsed().RowNumber(); rowNumber++)
        {
            if (worksheet.Cell(rowNumber, 1).GetString() == employeeName)
            {
                return rowNumber;
            }
        }

        throw new InvalidOperationException($"Could not find employee row '{employeeName}' in the exported worksheet.");
    }

    private static async Task<ExpectedPayrollRow> SeedUniquePayrollScenarioAsync(TestWebApplicationFactory factory, PayrollPeriod period)
    {
        return await factory.ExecuteDbContextAsync(context => SeedPayrollScenarioAsync(context, period, $"Payroll Scenario {Guid.NewGuid():N}"));
    }

    private static async Task<ExpectedPayrollRow> SeedPayrollScenarioAsync(OrgDbContext context, PayrollPeriod period, string employeeName)
    {
        var employee = TestDbHelpers.CreateEmployee(name: employeeName, hourlyPayRate: 33.25m);
        var shift = TestDbHelpers.CreateShift();

        context.Employees.Add(employee);
        context.Shifts.Add(shift);
        context.AttendanceRecords.AddRange(
            TestDbHelpers.CreateAttendanceRecord(employee, shift, period.StartDate, AdjustmentType.None, 8.00m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, period.StartDate.AddDays(1), AdjustmentType.Sick, 8.00m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, period.StartDate.AddDays(2), AdjustmentType.Vacation, 8.00m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, period.StartDate.AddDays(3), AdjustmentType.Late, 7.50m),
            TestDbHelpers.CreateAttendanceRecord(employee, shift, period.StartDate.AddDays(4), AdjustmentType.NoShow, 0.00m));

        await context.SaveChangesAsync();

        var row = (await new PayrollService(context).BuildReportAsync(period.StartDate, period.EndDate))
            .Single(reportRow => reportRow.EmployeeName == employeeName);

        return new ExpectedPayrollRow(
            row.EmployeeName,
            row.RegularHours,
            row.SickHours,
            row.VacationHours,
            row.LateHours,
            row.NoShowCount,
            row.TotalHoursToPay,
            row.GrossPay);
    }

    private readonly record struct PayrollPeriod(DateTime StartDate, DateTime EndDate);

    private readonly record struct ExpectedPayrollRow(
        string EmployeeName,
        decimal RegularHours,
        decimal SickHours,
        decimal VacationHours,
        decimal LateHours,
        int NoShowCount,
        decimal TotalHoursToPay,
        decimal GrossPay);
}
