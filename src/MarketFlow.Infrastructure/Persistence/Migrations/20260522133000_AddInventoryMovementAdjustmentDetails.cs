using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260522133000_AddInventoryMovementAdjustmentDetails")]
    public partial class AddInventoryMovementAdjustmentDetails : Migration
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
                            reason              VARCHAR(100),
                            note                TEXT,
                            created_by_user_id  INT,
                            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );
                        ALTER TABLE %1$I.inventory_movements
                            ADD COLUMN IF NOT EXISTS reason VARCHAR(100),
                            ADD COLUMN IF NOT EXISTS note TEXT;
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
                            EXECUTE format(
                                'ALTER TABLE %I.inventory_movements DROP COLUMN IF EXISTS note, DROP COLUMN IF EXISTS reason',
                                tenant.schema_name);
                        END IF;
                    END LOOP;
                END;
                $$;

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
                """);
        }
    }
}
