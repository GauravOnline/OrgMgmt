namespace OrgMgmt.ViewModels
{
    public class AdminUserViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public IList<string> Roles { get; set; } = new List<string>();
        public IList<string> AllRoles { get; set; } = new List<string>();
    }
}
