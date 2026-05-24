using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524140000_AddTenantSaleReferenceNumbers")]
    public partial class AddTenantSaleReferenceNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.assign_sale_reference_number()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.reference_number IS NULL OR btrim(NEW.reference_number) = '' THEN
                        NEW.reference_number := 'SALE-' || lpad(NEW.id::text, 6, '0');
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE OR REPLACE FUNCTION public.ensure_tenant_sale_reference_numbers(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    IF to_regclass(format('%I.sales', p_schema_name)) IS NULL THEN
                        RETURN;
                    END IF;

                    EXECUTE format($tenant$
                        ALTER TABLE %1$I.sales
                            ADD COLUMN IF NOT EXISTS reference_number VARCHAR(50);

                        UPDATE %1$I.sales
                        SET reference_number = 'SALE-' || lpad(id::text, 6, '0')
                        WHERE reference_number IS NULL OR btrim(reference_number) = '';

                        ALTER TABLE %1$I.sales
                            ALTER COLUMN reference_number SET NOT NULL;

                        CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_reference_number
                            ON %1$I.sales(reference_number);

                        DROP TRIGGER IF EXISTS trg_sales_assign_reference_number ON %1$I.sales;
                        CREATE TRIGGER trg_sales_assign_reference_number
                            BEFORE INSERT ON %1$I.sales
                            FOR EACH ROW
                            EXECUTE FUNCTION public.assign_sale_reference_number();
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
                    RETURN NEW;
                END;
                $$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        PERFORM public.ensure_tenant_sale_reference_numbers(tenant.schema_name);
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
                        IF tenant.schema_name ~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$'
                            AND to_regclass(format('%I.sales', tenant.schema_name)) IS NOT NULL THEN
                            EXECUTE format($tenant$
                                DROP TRIGGER IF EXISTS trg_sales_assign_reference_number ON %1$I.sales;
                                DROP INDEX IF EXISTS %1$I.ux_sales_reference_number;
                                ALTER TABLE %1$I.sales DROP COLUMN IF EXISTS reference_number;
                            $tenant$, tenant.schema_name);
                        END IF;
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
                    RETURN NEW;
                END;
                $$;

                DROP FUNCTION IF EXISTS public.ensure_tenant_sale_reference_numbers(text);
                DROP FUNCTION IF EXISTS public.assign_sale_reference_number();
                """);
        }
    }
}
