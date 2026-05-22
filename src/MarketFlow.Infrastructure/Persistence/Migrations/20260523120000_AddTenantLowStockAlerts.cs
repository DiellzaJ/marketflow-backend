using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260523120000_AddTenantLowStockAlerts")]
    public partial class AddTenantLowStockAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.ensure_tenant_low_stock_alerts_table(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    EXECUTE format($tenant$
                        CREATE TABLE IF NOT EXISTS %1$I.low_stock_alerts (
                            id                BIGSERIAL PRIMARY KEY,
                            inventory_id      INT         NOT NULL REFERENCES %1$I.inventory(id) ON DELETE CASCADE,
                            product_id        INT         NOT NULL REFERENCES %1$I.products(id) ON DELETE CASCADE,
                            market_id         INT         NOT NULL REFERENCES %1$I.markets(id) ON DELETE CASCADE,
                            department_id     INT         REFERENCES %1$I.departments(id) ON DELETE SET NULL,
                            quantity          INT         NOT NULL,
                            min_stock_alert   INT         NOT NULL,
                            status            VARCHAR(20) NOT NULL DEFAULT 'Active'
                                CHECK (status IN ('Active', 'Resolved')),
                            first_detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                            last_detected_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                            resolved_at       TIMESTAMPTZ
                        );
                        CREATE INDEX IF NOT EXISTS idx_low_stock_alerts_inventory
                            ON %1$I.low_stock_alerts(inventory_id);
                        CREATE INDEX IF NOT EXISTS idx_low_stock_alerts_status
                            ON %1$I.low_stock_alerts(status);
                        CREATE UNIQUE INDEX IF NOT EXISTS ux_low_stock_alerts_active_product_location
                            ON %1$I.low_stock_alerts(product_id, market_id, (COALESCE(department_id, -1)))
                            WHERE status = 'Active';
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
                    RETURN NEW;
                END;
                $$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        PERFORM public.ensure_tenant_low_stock_alerts_table(tenant.schema_name);
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
                        IF tenant.schema_name ~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                            EXECUTE format('DROP TABLE IF EXISTS %I.low_stock_alerts', tenant.schema_name);
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
                    RETURN NEW;
                END;
                $$;

                DROP FUNCTION IF EXISTS public.ensure_tenant_low_stock_alerts_table(text);
                """);
        }
    }
}
