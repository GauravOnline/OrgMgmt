using System.Security.Claims;

namespace OrgMgmt.Services
{
    public interface IFinancialDataAuthorizationService
    {
        bool CanViewFinancialData(ClaimsPrincipal user);
    }

    public class FinancialDataAuthorizationService : IFinancialDataAuthorizationService
    {
        public bool CanViewFinancialData(ClaimsPrincipal user)
        {
            return user.IsInRole("Admin") || user.IsInRole("HR") || user.IsInRole("Payroll");
        }
    }
}
