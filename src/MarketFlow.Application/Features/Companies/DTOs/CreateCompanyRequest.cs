namespace MarketFlow.Application.Features.Companies.DTOs;

public class CreateCompanyRequest
{
    public string Name { get; set; } = string.Empty;

    public string CompanyType { get; set; } = "SMALL";

    public string? SchemaName { get; set; }
}
