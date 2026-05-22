using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260522120000_AddTenantInventoryMovements")]
    public partial class AddTenantInventoryMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.ensure_tenant_inventory_movements_table(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    EXECUTE format($tenant$
                        CREATE TABLE IF NOT EXISTS %1$I.inventory_movements (
                            id                  SERIAL PRIMARY KEY,
                            inventory_id        INT         NOT NULL REFERENCES %1$I.inventory(id) ON DELETE CASCADE,
                            movement_type       VARCHAR(30) NOT NULL,
                            quantity_changed    INT         NOT NULL,
                            reference_number    VARCHAR(100),
                            created_by_user_id  INT,
                            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );
                        CREATE INDEX IF NOT EXISTS idx_inventory_movements_inventory ON %1$I.inventory_movements(inventory_id);
                        CREATE INDEX IF NOT EXISTS idx_inventory_movements_created ON %1$I.inventory_movements(created_at);
                    $tenant$, p_schema_name);
                END;
                $$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        PERFORM public.ensure_tenant_inventory_movements_table(tenant.schema_name);
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
                            EXECUTE format('DROP TABLE IF EXISTS %I.inventory_movements', tenant.schema_name);
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
                    RETURN NEW;
                END;
                $$;

                DROP FUNCTION IF EXISTS public.ensure_tenant_inventory_movements_table(text);
                """);
        }
    }
}
