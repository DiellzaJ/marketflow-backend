using MarketFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketFlow.Infrastructure.Persistence.Configurations;

public class AiAnalysisRequestConfiguration : IEntityTypeConfiguration<AiAnalysisRequest>
{
    public void Configure(EntityTypeBuilder<AiAnalysisRequest> builder)
    {
        builder.ToTable("ai_analysis_requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(x => x.AnalysisType)
            .HasColumnName("analysis_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Prompt)
            .HasColumnName("prompt")
            .IsRequired();

        builder.Property(x => x.RequestPayload)
            .HasColumnName("request_payload")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message");

        builder.Property(x => x.RequestedByUserId)
            .HasColumnName("requested_by_user_id");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAt);
    }
}
