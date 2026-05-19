using System.Text;
using System.Text.RegularExpressions;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Application.Features.Companies.Interfaces;

namespace MarketFlow.Application.Features.Companies.Services;

public class CompanyService : ICompanyService
{
    private static readonly Regex SchemaNamePattern = new(
        "^[a-z][a-z0-9_]{0,62}$",
        RegexOptions.Compiled);

    private static readonly Regex UnsafeSchemaCharacters = new(
        "[^a-z0-9_]+",
        RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedCompanyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SMALL",
        "MEDIUM",
        "BIG"
    };

    private readonly ICompanyStore _companyStore;

    public CompanyService(ICompanyStore companyStore)
    {
        _companyStore = companyStore;
    }

    public async Task<ServiceResult<IReadOnlyCollection<CompanyDto>>> GetCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        var companies = await _companyStore.GetCompaniesAsync(cancellationToken);

        return ServiceResult<IReadOnlyCollection<CompanyDto>>.Success(companies);
    }

    public async Task<ServiceResult<CompanyDto>> CreateCompanyAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var companyType = (request.CompanyType ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<CompanyDto>.Failure("Company name is required.");
        }

        if (name.Length > 150)
        {
            return ServiceResult<CompanyDto>.Failure("Company name cannot exceed 150 characters.");
        }

        if (!AllowedCompanyTypes.Contains(companyType))
        {
            return ServiceResult<CompanyDto>.Failure("Company type must be SMALL, MEDIUM, or BIG.");
        }

        var schemaName = string.IsNullOrWhiteSpace(request.SchemaName)
            ? GenerateSchemaName(name)
            : request.SchemaName.Trim().ToLowerInvariant();

        if (!SchemaNamePattern.IsMatch(schemaName))
        {
            return ServiceResult<CompanyDto>.Failure(
                "Schema name must start with a lowercase letter and contain only lowercase letters, numbers, and underscores.");
        }

        if (await _companyStore.SchemaNameExistsAsync(schemaName, cancellationToken))
        {
            return ServiceResult<CompanyDto>.Failure("Schema name is already used.");
        }

        var createRequest = new CreateCompanyRequest
        {
            Name = name,
            CompanyType = companyType,
            SchemaName = schemaName
        };

        var company = await _companyStore.CreateCompanyAsync(
            createRequest,
            schemaName,
            cancellationToken);

        return company is null
            ? ServiceResult<CompanyDto>.Failure("Company could not be created.")
            : ServiceResult<CompanyDto>.Success(company, "Company created.");
    }

    private static string GenerateSchemaName(string companyName)
    {
        var normalized = companyName.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = char.GetUnicodeCategory(character);

            if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        var schemaName = UnsafeSchemaCharacters
            .Replace(builder.ToString().Normalize(NormalizationForm.FormC), "_")
            .Trim('_');

        if (string.IsNullOrWhiteSpace(schemaName) || !char.IsLetter(schemaName[0]))
        {
            schemaName = $"tenant_{schemaName}";
        }

        return schemaName.Length <= 63 ? schemaName : schemaName[..63].TrimEnd('_');
    }
}
