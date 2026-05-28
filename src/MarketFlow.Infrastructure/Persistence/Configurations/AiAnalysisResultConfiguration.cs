using MarketFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketFlow.Infrastructure.Persistence.Configurations;

public class AiAnalysisResultConfiguration : IEntityTypeConfiguration<AiAnalysisResult>
{
    public void Configure(EntityTypeBuilder<AiAnalysisResult> builder)
    {
        builder.ToTable("ai_analysis_results");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(x => x.AiAnalysisRequestId)
            .HasColumnName("ai_analysis_request_id")
            .IsRequired();

        builder.Property(x => x.Model)
            .HasColumnName("model")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ResultPayload)
            .HasColumnName("result_payload")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(x => x.Summary)
            .HasColumnName("summary");

        builder.Property(x => x.PromptTokens)
            .HasColumnName("prompt_tokens");

        builder.Property(x => x.CompletionTokens)
            .HasColumnName("completion_tokens");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.AiAnalysisRequestId);

        builder.HasOne(x => x.Request)
            .WithMany(x => x.Results)
            .HasForeignKey(x => x.AiAnalysisRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
