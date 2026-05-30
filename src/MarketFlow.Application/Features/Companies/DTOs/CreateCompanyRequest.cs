using System.ComponentModel.DataAnnotations;

namespace MarketFlow.Application.Features.Companies.DTOs;

public class CreateCompanyRequest
{
    [Required(ErrorMessage = "Company name is required.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Company name must be between 1 and 150 characters.")]
    public string Name { get; set; } = string.Empty;

    [RegularExpression("(?i)^(SMALL|MEDIUM|BIG)$", ErrorMessage = "Company type must be SMALL, MEDIUM, or BIG.")]
    public string CompanyType { get; set; } = "SMALL";

    [RegularExpression(
        "(?i)^[a-z][a-z0-9_]{0,62}$",
        ErrorMessage = "Schema name must start with a letter and contain only letters, numbers, and underscores.")]
    public string? SchemaName { get; set; }

    [Required]
    public CreateCompanyAdminRequest CompanyAdmin { get; set; } = new();
}
