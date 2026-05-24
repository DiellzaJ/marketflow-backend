using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524143000_OptimizeTenantSaleReferenceNumberEnsureFunction")]
    public partial class OptimizeTenantSaleReferenceNumberEnsureFunction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.ensure_tenant_sale_reference_numbers(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    reference_number_column_exists boolean;
                    reference_number_column_is_nullable boolean;
                    reference_number_index_exists boolean;
                    reference_number_trigger_exists boolean;
                    reference_numbers_need_backfill boolean;
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    IF to_regclass(format('%I.sales', p_schema_name)) IS NULL THEN
                        RETURN;
                    END IF;

                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = p_schema_name
                          AND table_name = 'sales'
                          AND column_name = 'reference_number'
                    )
                    INTO reference_number_column_exists;

                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = p_schema_name
                          AND table_name = 'sales'
                          AND column_name = 'reference_number'
                          AND is_nullable = 'YES'
                    )
                    INTO reference_number_column_is_nullable;

                    SELECT to_regclass(format('%I.ux_sales_reference_number', p_schema_name)) IS NOT NULL
                    INTO reference_number_index_exists;

                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_catalog.pg_trigger trigger
                        INNER JOIN pg_catalog.pg_class relation ON relation.oid = trigger.tgrelid
                        INNER JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                        WHERE namespace.nspname = p_schema_name
                          AND relation.relname = 'sales'
                          AND trigger.tgname = 'trg_sales_assign_reference_number'
                    )
                    INTO reference_number_trigger_exists;

                    IF reference_number_column_exists THEN
                        EXECUTE format($tenant$
                            SELECT EXISTS (
                                SELECT 1
                                FROM %1$I.sales
                                WHERE reference_number IS NULL OR btrim(reference_number) = ''
                            );
                        $tenant$, p_schema_name)
                        INTO reference_numbers_need_backfill;
                    ELSE
                        reference_numbers_need_backfill := FALSE;
                    END IF;

                    IF reference_number_column_exists
                        AND NOT reference_number_column_is_nullable
                        AND NOT reference_numbers_need_backfill
                        AND reference_number_index_exists
                        AND reference_number_trigger_exists THEN
                        RETURN;
                    END IF;

                    IF NOT reference_number_column_exists THEN
                        EXECUTE format($tenant$
                            ALTER TABLE %1$I.sales
                                ADD COLUMN IF NOT EXISTS reference_number VARCHAR(50);
                        $tenant$, p_schema_name);
                    END IF;

                    IF NOT reference_number_column_exists OR NOT reference_number_index_exists OR reference_numbers_need_backfill THEN
                        EXECUTE format($tenant$
                            UPDATE %1$I.sales
                            SET reference_number = 'SALE-' || lpad(id::text, 6, '0')
                            WHERE reference_number IS NULL OR btrim(reference_number) = '';
                        $tenant$, p_schema_name);
                    END IF;

                    IF NOT reference_number_column_exists OR reference_number_column_is_nullable OR reference_numbers_need_backfill THEN
                        EXECUTE format($tenant$
                            ALTER TABLE %1$I.sales
                                ALTER COLUMN reference_number SET NOT NULL;
                        $tenant$, p_schema_name);
                    END IF;

                    IF NOT reference_number_index_exists THEN
                        EXECUTE format($tenant$
                            CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_reference_number
                                ON %1$I.sales(reference_number);
                        $tenant$, p_schema_name);
                    END IF;

                    IF NOT reference_number_trigger_exists THEN
                        EXECUTE format($tenant$
                            CREATE TRIGGER trg_sales_assign_reference_number
                                BEFORE INSERT ON %1$I.sales
                                FOR EACH ROW
                                EXECUTE FUNCTION public.assign_sale_reference_number();
                        $tenant$, p_schema_name);
                    END IF;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
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
                """);
        }
    }
}
