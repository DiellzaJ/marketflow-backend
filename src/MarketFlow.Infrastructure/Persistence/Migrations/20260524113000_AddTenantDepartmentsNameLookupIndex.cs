using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524113000_AddTenantDepartmentsNameLookupIndex")]
    public partial class AddTenantDepartmentsNameLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        IF to_regclass(format('%I.departments', tenant.schema_name)) IS NOT NULL THEN
                            EXECUTE format(
                                'CREATE INDEX IF NOT EXISTS idx_departments_market_name_lower ON %I.departments (market_id, lower(name));',
                                tenant.schema_name);
                        END IF;
                    END LOOP;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        IF to_regnamespace(tenant.schema_name) IS NOT NULL THEN
                            EXECUTE format(
                                'DROP INDEX IF EXISTS %I.idx_departments_market_name_lower;',
                                tenant.schema_name);
                        END IF;
                    END LOOP;
                END;
                $$;
                """);
        }
    }
}
