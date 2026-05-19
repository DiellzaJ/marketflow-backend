namespace MarketFlow.Application.Features.Companies.DTOs;

public class CompanyOnboardingDto
{
    public CompanyDto Company { get; set; } = new();

    public CompanyAdminSummaryDto CompanyAdmin { get; set; } = new();
}
