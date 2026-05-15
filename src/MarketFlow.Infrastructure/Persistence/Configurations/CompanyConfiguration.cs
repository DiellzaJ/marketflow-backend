using MarketFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketFlow.Infrastructure.Persistence.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.SchemaName)
            .HasColumnName("schema_name")
            .HasMaxLength(63)
            .IsRequired();

        builder.HasIndex(x => x.SchemaName)
            .IsUnique();

        builder.Property(x => x.CompanyType)
            .HasColumnName("company_type")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.SubscriptionPlan)
            .HasColumnName("subscription_plan")
            .HasMaxLength(30)
            .HasDefaultValue("BASIC")
            .IsRequired();

        builder.Property(x => x.MaxMarkets)
            .HasColumnName("max_markets")
            .HasDefaultValue(5)
            .IsRequired();

        builder.Property(x => x.MaxUsers)
            .HasColumnName("max_users")
            .HasDefaultValue(50)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()")
            .IsRequired();
    }
}