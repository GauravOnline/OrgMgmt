using System.ComponentModel.DataAnnotations;

namespace OrgMgmt.ViewModels
{
    public class RegisterViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required, DataType(DataType.Password), MinLength(6)]
        public string Password { get; set; } = string.Empty;
        [DataType(DataType.Password), Compare("Password", ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
