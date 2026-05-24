using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524190000_AddTenantPurchaseReceivingWorkflow")]
    public partial class AddTenantPurchaseReceivingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.ensure_tenant_purchase_receiving_workflow(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    EXECUTE format($tenant$
                        UPDATE %1$I.purchases
                        SET status = CASE status
                            WHEN 'Pending' THEN 'Ordered'
                            ELSE status
                        END;

                        ALTER TABLE %1$I.purchases
                            ALTER COLUMN status SET DEFAULT 'Draft';

                        ALTER TABLE %1$I.purchases
                            DROP CONSTRAINT IF EXISTS purchases_status_check;

                        ALTER TABLE %1$I.purchases
                            ADD CONSTRAINT purchases_status_check
                            CHECK (status IN ('Pending', 'Draft', 'Ordered', 'PartiallyReceived', 'Received', 'Cancelled'));

                        ALTER TABLE %1$I.purchase_items
                            ADD COLUMN IF NOT EXISTS received_quantity INT NOT NULL DEFAULT 0;

                        UPDATE %1$I.purchase_items pi
                        SET received_quantity = pi.quantity
                        FROM %1$I.purchases p
                        WHERE p.id = pi.purchase_id
                          AND p.status = 'Received'
                          AND pi.received_quantity = 0;

                        ALTER TABLE %1$I.purchase_items
                            DROP CONSTRAINT IF EXISTS purchase_items_received_quantity_check;

                        ALTER TABLE %1$I.purchase_items
                            ADD CONSTRAINT purchase_items_received_quantity_check
                            CHECK (received_quantity >= 0 AND received_quantity <= quantity);

                        CREATE INDEX IF NOT EXISTS idx_purchase_items_product
                            ON %1$I.purchase_items(product_id);
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
                    RETURN NEW;
                END;
                $$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        PERFORM public.ensure_tenant_purchase_receiving_workflow(tenant.schema_name);
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
                        EXECUTE format('DROP INDEX IF EXISTS %I.idx_purchase_items_product', tenant.schema_name);
                        EXECUTE format('ALTER TABLE %I.purchase_items DROP CONSTRAINT IF EXISTS purchase_items_received_quantity_check', tenant.schema_name);
                        EXECUTE format('ALTER TABLE %I.purchase_items DROP COLUMN IF EXISTS received_quantity', tenant.schema_name);
                        EXECUTE format('ALTER TABLE %I.purchases DROP CONSTRAINT IF EXISTS purchases_status_check', tenant.schema_name);
                        EXECUTE format('ALTER TABLE %I.purchases ALTER COLUMN status SET DEFAULT ''Pending''', tenant.schema_name);
                        EXECUTE format('ALTER TABLE %I.purchases ADD CONSTRAINT purchases_status_check CHECK (status IN (''Pending'', ''Received'', ''Cancelled''))', tenant.schema_name);
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
                    RETURN NEW;
                END;
                $$;

                DROP FUNCTION IF EXISTS public.ensure_tenant_purchase_receiving_workflow(text);
                """);
        }
    }
}
