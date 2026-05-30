using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260527130000_AddTenantAiAnalysisTables")]
    public partial class AddTenantAiAnalysisTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pgcrypto;

                CREATE OR REPLACE FUNCTION public.ensure_tenant_ai_analysis_tables(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    EXECUTE format($tenant$
                        CREATE TABLE IF NOT EXISTS %1$I.ai_analysis_requests (
                            id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                            company_id           UUID        NOT NULL,
                            analysis_type        VARCHAR(100) NOT NULL,
                            prompt               TEXT        NOT NULL,
                            request_payload      JSONB       NOT NULL DEFAULT '{}'::jsonb,
                            status               VARCHAR(20) NOT NULL DEFAULT 'Pending',
                            error_message        TEXT,
                            requested_by_user_id INT,
                            created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                            updated_at           TIMESTAMPTZ
                        );

                        ALTER TABLE %1$I.ai_analysis_requests
                            DROP CONSTRAINT IF EXISTS ai_analysis_requests_status_check;

                        ALTER TABLE %1$I.ai_analysis_requests
                            ADD CONSTRAINT ai_analysis_requests_status_check
                            CHECK (status IN ('Pending', 'Processing', 'Completed', 'Failed'));

                        CREATE INDEX IF NOT EXISTS idx_ai_analysis_requests_company
                            ON %1$I.ai_analysis_requests(company_id);

                        CREATE INDEX IF NOT EXISTS idx_ai_analysis_requests_status
                            ON %1$I.ai_analysis_requests(status);

                        CREATE INDEX IF NOT EXISTS idx_ai_analysis_requests_created
                            ON %1$I.ai_analysis_requests(created_at);

                        CREATE TABLE IF NOT EXISTS %1$I.ai_analysis_results (
                            id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                            company_id             UUID        NOT NULL,
                            ai_analysis_request_id UUID        NOT NULL REFERENCES %1$I.ai_analysis_requests(id) ON DELETE CASCADE,
                            model                  VARCHAR(100) NOT NULL,
                            result_payload         JSONB       NOT NULL DEFAULT '{}'::jsonb,
                            summary                TEXT,
                            prompt_tokens          INT,
                            completion_tokens      INT,
                            created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                            updated_at             TIMESTAMPTZ
                        );

                        CREATE INDEX IF NOT EXISTS idx_ai_analysis_results_company
                            ON %1$I.ai_analysis_results(company_id);

                        CREATE INDEX IF NOT EXISTS idx_ai_analysis_results_request
                            ON %1$I.ai_analysis_results(ai_analysis_request_id);

                        CREATE TABLE IF NOT EXISTS %1$I.ai_chat_sessions (
                            id         BIGSERIAL PRIMARY KEY,
                            user_id    INT         NOT NULL,
                            market_id  INT         REFERENCES %1$I.markets(id) ON DELETE SET NULL,
                            messages   JSONB       NOT NULL DEFAULT '[]'::jsonb,
                            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );

                        ALTER TABLE %1$I.ai_chat_sessions
                            ADD COLUMN IF NOT EXISTS title VARCHAR(200);

                        ALTER TABLE %1$I.ai_chat_sessions
                            ADD COLUMN IF NOT EXISTS company_id UUID;

                        ALTER TABLE %1$I.ai_chat_sessions
                            ALTER COLUMN messages SET DEFAULT '[]'::jsonb;

                        ALTER TABLE %1$I.ai_chat_sessions
                            DROP CONSTRAINT IF EXISTS ai_chat_sessions_messages_array_check;

                        ALTER TABLE %1$I.ai_chat_sessions
                            ADD CONSTRAINT ai_chat_sessions_messages_array_check
                            CHECK (jsonb_typeof(messages) = 'array');

                        CREATE INDEX IF NOT EXISTS idx_ai_sessions_company
                            ON %1$I.ai_chat_sessions(company_id);

                        CREATE INDEX IF NOT EXISTS idx_ai_sessions_user
                            ON %1$I.ai_chat_sessions(user_id);

                        CREATE INDEX IF NOT EXISTS idx_ai_sessions_created
                            ON %1$I.ai_chat_sessions(created_at);
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
                    PERFORM public.ensure_tenant_ai_analysis_tables(NEW.schema_name);
                    RETURN NEW;
                END;
                $$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        PERFORM public.ensure_tenant_ai_analysis_tables(tenant.schema_name);
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
                            EXECUTE format('DROP TABLE IF EXISTS %I.ai_analysis_results', tenant.schema_name);
                            EXECUTE format('DROP TABLE IF EXISTS %I.ai_analysis_requests', tenant.schema_name);
                            EXECUTE format('DROP INDEX IF EXISTS %I.idx_ai_sessions_company', tenant.schema_name);
                            EXECUTE format('DROP INDEX IF EXISTS %I.idx_ai_sessions_created', tenant.schema_name);
                            EXECUTE format('ALTER TABLE %I.ai_chat_sessions DROP CONSTRAINT IF EXISTS ai_chat_sessions_messages_array_check', tenant.schema_name);
                            EXECUTE format('ALTER TABLE %I.ai_chat_sessions DROP COLUMN IF EXISTS company_id', tenant.schema_name);
                            EXECUTE format('ALTER TABLE %I.ai_chat_sessions DROP COLUMN IF EXISTS title', tenant.schema_name);
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
                    PERFORM public.ensure_tenant_sale_reference_numbers(NEW.schema_name);
                    PERFORM public.ensure_tenant_sales_department_scope(NEW.schema_name);
                    PERFORM public.ensure_tenant_sales_status(NEW.schema_name);
                    PERFORM public.ensure_tenant_purchase_receiving_workflow(NEW.schema_name);
                    PERFORM public.ensure_tenant_purchase_expected_date(NEW.schema_name);
                    RETURN NEW;
                END;
                $$;

                DROP FUNCTION IF EXISTS public.ensure_tenant_ai_analysis_tables(text);
                """);
        }
    }
}
