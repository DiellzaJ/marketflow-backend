namespace MarketFlow.Application.Features.Companies.DTOs;

public class CompanyDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    public string CompanyType { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
