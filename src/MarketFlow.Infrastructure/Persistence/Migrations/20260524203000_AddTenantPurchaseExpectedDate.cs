using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524203000_AddTenantPurchaseExpectedDate")]
    public partial class AddTenantPurchaseExpectedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.ensure_tenant_purchase_expected_date(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    EXECUTE format($tenant$
                        ALTER TABLE %1$I.purchases
                            ADD COLUMN IF NOT EXISTS expected_date DATE;
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
                    PERFORM public.ensure_tenant_purchase_receiving_workflow(NEW.schema_name);
                    PERFORM public.ensure_tenant_purchase_expected_date(NEW.schema_name);
                    RETURN NEW;
                END;
                $$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        PERFORM public.ensure_tenant_purchase_expected_date(tenant.schema_name);
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
                        EXECUTE format('ALTER TABLE %I.purchases DROP COLUMN IF EXISTS expected_date', tenant.schema_name);
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
                    PERFORM public.ensure_tenant_sales_status(NEW.schema_name);
                    PERFORM public.ensure_tenant_purchase_receiving_workflow(NEW.schema_name);
                    RETURN NEW;
                END;
                $$;

                DROP FUNCTION IF EXISTS public.ensure_tenant_purchase_expected_date(text);
                """);
        }
    }
}
