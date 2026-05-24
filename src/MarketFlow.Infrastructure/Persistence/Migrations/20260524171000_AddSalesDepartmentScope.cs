using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524171000_AddSalesDepartmentScope")]
    public partial class AddSalesDepartmentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.ensure_tenant_sales_department_scope(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    EXECUTE format($tenant$
                        ALTER TABLE %1$I.sales
                            ADD COLUMN IF NOT EXISTS department_id INT;

                        CREATE INDEX IF NOT EXISTS idx_sales_department
                            ON %1$I.sales(department_id);

                        UPDATE %1$I.sales s
                        SET department_id = scoped.department_id
                        FROM (
                            SELECT split_part(im.reference_number, ':', 2)::int AS sale_id,
                                   min(i.department_id) AS department_id
                            FROM %1$I.inventory_movements im
                            INNER JOIN %1$I.inventory i ON i.id = im.inventory_id
                            WHERE im.reference_number ~ '^sale:[0-9]+$'
                              AND i.department_id IS NOT NULL
                            GROUP BY split_part(im.reference_number, ':', 2)::int
                            HAVING count(DISTINCT i.department_id) = 1
                        ) scoped
                        WHERE s.id = scoped.sale_id
                          AND s.department_id IS NULL;
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
                    RETURN NEW;
                END;
                $$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        PERFORM public.ensure_tenant_sales_department_scope(tenant.schema_name);
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
                        EXECUTE format('DROP INDEX IF EXISTS %I.idx_sales_department', tenant.schema_name);
                        EXECUTE format('ALTER TABLE %I.sales DROP COLUMN IF EXISTS department_id', tenant.schema_name);
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
                    RETURN NEW;
                END;
                $$;

                DROP FUNCTION IF EXISTS public.ensure_tenant_sales_department_scope(text);
                """);
        }
    }
}
