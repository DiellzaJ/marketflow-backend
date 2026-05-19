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

    public async Task<ServiceResult<CompanyDto>> GetCompanyByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var company = await _companyStore.GetCompanyByIdAsync(id, cancellationToken);

        return company is null
            ? ServiceResult<CompanyDto>.Failure("Company was not found.")
            : ServiceResult<CompanyDto>.Success(company);
    }

    public async Task<ServiceResult<CompanyOnboardingDto>> CreateCompanyAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var companyType = (request.CompanyType ?? string.Empty).Trim().ToUpperInvariant();
        var adminFullName = (request.CompanyAdmin?.FullName ?? string.Empty).Trim();
        var adminEmail = (request.CompanyAdmin?.Email ?? string.Empty).Trim().ToLowerInvariant();
        var adminPassword = request.CompanyAdmin?.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<CompanyOnboardingDto>.Failure("Company name is required.");
        }

        if (name.Length > 150)
        {
            return ServiceResult<CompanyOnboardingDto>.Failure("Company name cannot exceed 150 characters.");
        }

        if (!AllowedCompanyTypes.Contains(companyType))
        {
            return ServiceResult<CompanyOnboardingDto>.Failure("Company type must be SMALL, MEDIUM, or BIG.");
        }

        if (string.IsNullOrWhiteSpace(adminFullName) ||
            string.IsNullOrWhiteSpace(adminEmail) ||
            string.IsNullOrWhiteSpace(adminPassword))
        {
            return ServiceResult<CompanyOnboardingDto>.Failure(
                "Company admin full name, email, and password are required.");
        }

        if (adminFullName.Length > 150)
        {
            return ServiceResult<CompanyOnboardingDto>.Failure(
                "Company admin full name cannot exceed 150 characters.");
        }

        if (adminEmail.Length > 255)
        {
            return ServiceResult<CompanyOnboardingDto>.Failure(
                "Company admin email cannot exceed 255 characters.");
        }

        if (adminPassword.Length < 8)
        {
            return ServiceResult<CompanyOnboardingDto>.Failure(
                "Company admin password must be at least 8 characters.");
        }

        var schemaName = string.IsNullOrWhiteSpace(request.SchemaName)
            ? GenerateSchemaName(name)
            : request.SchemaName.Trim().ToLowerInvariant();

        if (!SchemaNamePattern.IsMatch(schemaName))
        {
            return ServiceResult<CompanyOnboardingDto>.Failure(
                "Schema name must start with a lowercase letter and contain only lowercase letters, numbers, and underscores.");
        }

        if (await _companyStore.SchemaNameExistsAsync(schemaName, cancellationToken))
        {
            return ServiceResult<CompanyOnboardingDto>.Failure("Schema name is already used.");
        }

        if (await _companyStore.EmailExistsAsync(adminEmail, cancellationToken))
        {
            return ServiceResult<CompanyOnboardingDto>.Failure("Company admin email is already used.");
        }

        var createRequest = new CreateCompanyRequest
        {
            Name = name,
            CompanyType = companyType,
            SchemaName = schemaName,
            CompanyAdmin = new CreateCompanyAdminRequest
            {
                FullName = adminFullName,
                Email = adminEmail,
                Password = adminPassword
            }
        };

        var company = await _companyStore.CreateCompanyAsync(
            createRequest,
            schemaName,
            cancellationToken);

        return company is null
            ? ServiceResult<CompanyOnboardingDto>.Failure("Company could not be created.")
            : ServiceResult<CompanyOnboardingDto>.Success(company, "Company created.");
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

        if (string.IsNullOrWhiteSpace(schemaName))
        {
            schemaName = $"tenant_{Guid.NewGuid():N}"[..15];
        }
        else if (!char.IsLetter(schemaName[0]))
        {
            schemaName = $"tenant_{schemaName}";
        }

        return schemaName.Length <= 63 ? schemaName : schemaName[..63].TrimEnd('_');
    }
}
