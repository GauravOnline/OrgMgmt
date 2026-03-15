using System.Security.Claims;
using OrgMgmt.Services;
using Xunit;

namespace OrgMgmt.Tests;

public class FinancialDataAuthorizationServiceTests
{
    private readonly FinancialDataAuthorizationService _service = new();

    private static ClaimsPrincipal CreateUserWithRoles(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "testuser@example.com")
        };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateUnauthenticatedUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("HR")]
    [InlineData("Payroll")]
    public void CanViewFinancialData_ReturnsTrue_ForAuthorizedRoles(string role)
    {
        var user = CreateUserWithRoles(role);
        Assert.True(_service.CanViewFinancialData(user));
    }

    [Theory]
    [InlineData("ScheduleManager")]
    [InlineData("Employee")]
    [InlineData("DirectManager")]
    public void CanViewFinancialData_ReturnsFalse_ForUnauthorizedRoles(string role)
    {
        var user = CreateUserWithRoles(role);
        Assert.False(_service.CanViewFinancialData(user));
    }

    [Fact]
    public void CanViewFinancialData_ReturnsFalse_ForUnauthenticatedUser()
    {
        var user = CreateUnauthenticatedUser();
        Assert.False(_service.CanViewFinancialData(user));
    }
}
