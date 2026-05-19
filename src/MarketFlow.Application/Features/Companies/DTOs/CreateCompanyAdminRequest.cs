using System.ComponentModel.DataAnnotations;

namespace MarketFlow.Application.Features.Companies.DTOs;

public class CreateCompanyAdminRequest
{
    [Required(ErrorMessage = "Company admin full name is required.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Company admin full name must be between 1 and 150 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Company admin email is required.")]
    [EmailAddress(ErrorMessage = "Company admin email must be a valid email address.")]
    [StringLength(255, ErrorMessage = "Company admin email cannot exceed 255 characters.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Company admin password is required.")]
    [StringLength(200, MinimumLength = 8, ErrorMessage = "Company admin password must be at least 8 characters.")]
    public string Password { get; set; } = string.Empty;
}
