using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524173000_AddTenantSalesStatus")]
    public partial class AddTenantSalesStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.ensure_tenant_sales_status(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    EXECUTE format($tenant$
                        ALTER TABLE %1$I.sales
                            ADD COLUMN IF NOT EXISTS status VARCHAR(20) NOT NULL DEFAULT 'Paid';

                        ALTER TABLE %1$I.sales
                            DROP CONSTRAINT IF EXISTS sales_status_check;

                        ALTER TABLE %1$I.sales
                            ADD CONSTRAINT sales_status_check
                            CHECK (status IN ('Draft', 'Pending', 'Paid', 'Cancelled'));

                        CREATE INDEX IF NOT EXISTS idx_sales_status
                            ON %1$I.sales(status);
                    $tenant$, p_schema_name);
                END;
                $$;

                CREATE OR REPLACE FUNCTION public.create_tenant_schema_for_company()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    PERFORM public.create_tenant_schema(NEW.schema_name);
                    PERFORM public.ensure_tenant_inventory_movements_table(NEW.schema_name);
                    PERFORM public.ensure_tenant_low_stock_alerts_table(NEW.schema_name);
                    PERFORM public.ensure_tenant_sale_reference_numbers(NEW.schema_name);
                    PERFORM public.ensure_tenant_sales_department_scope(NEW.schema_name);
                    PERFORM public.ensure_tenant_sales_status(NEW.schema_name);
                    RETURN NEW;
                END;
                $$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        PERFORM public.ensure_tenant_sales_status(tenant.schema_name);
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
                        EXECUTE format('DROP INDEX IF EXISTS %I.idx_sales_status', tenant.schema_name);
                        EXECUTE format('ALTER TABLE %I.sales DROP COLUMN IF EXISTS status', tenant.schema_name);
                    END LOOP;
                END;
                $$;

                CREATE OR REPLACE FUNCTION public.create_tenant_schema_for_company()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    PERFORM public.create_tenant_schema(NEW.schema_name);
                    PERFORM public.ensure_tenant_inventory_movements_table(NEW.schema_name);
                    PERFORM public.ensure_tenant_low_stock_alerts_table(NEW.schema_name);
                    PERFORM public.ensure_tenant_sale_reference_numbers(NEW.schema_name);
                    PERFORM public.ensure_tenant_sales_department_scope(NEW.schema_name);
                    RETURN NEW;
                END;
                $$;

                DROP FUNCTION IF EXISTS public.ensure_tenant_sales_status(text);
                """);
        }
    }
}
