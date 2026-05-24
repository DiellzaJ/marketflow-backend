using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260516120000_AddTenantSchemaProvisioning")]
    public partial class AddTenantSchemaProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.create_tenant_schema(p_schema_name text)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF p_schema_name IS NULL OR p_schema_name !~ '^[a-zA-Z_][a-zA-Z0-9_]{0,62}$' THEN
                        RAISE EXCEPTION 'Invalid tenant schema name: %', p_schema_name;
                    END IF;

                    EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I', p_schema_name);

                    EXECUTE format($tenant$
                        CREATE TABLE IF NOT EXISTS %1$I.markets (
                            id         SERIAL PRIMARY KEY,
                            name       VARCHAR(150) NOT NULL,
                            city       VARCHAR(100),
                            address    TEXT,
                            phone      VARCHAR(30),
                            is_active  BOOLEAN     NOT NULL DEFAULT TRUE,
                            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );
                        CREATE INDEX IF NOT EXISTS idx_markets_name_lower ON %1$I.markets (lower(name));

                        CREATE TABLE IF NOT EXISTS %1$I.departments (
                            id          SERIAL PRIMARY KEY,
                            market_id   INT         NOT NULL REFERENCES %1$I.markets(id) ON DELETE CASCADE,
                            name        VARCHAR(100) NOT NULL,
                            description TEXT,
                            is_active   BOOLEAN     NOT NULL DEFAULT TRUE,
                            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );
                        CREATE INDEX IF NOT EXISTS idx_departments_market ON %1$I.departments(market_id);
                        CREATE INDEX IF NOT EXISTS idx_departments_market_name_lower ON %1$I.departments(market_id, lower(name));

                        CREATE TABLE IF NOT EXISTS %1$I.staff_assignments (
                            id            SERIAL PRIMARY KEY,
                            user_id       INT         NOT NULL,
                            market_id     INT         NOT NULL REFERENCES %1$I.markets(id) ON DELETE CASCADE,
                            department_id INT         REFERENCES %1$I.departments(id) ON DELETE SET NULL,
                            job_title     VARCHAR(100),
                            assigned_by   INT,
                            valid_until   DATE,
                            is_active     BOOLEAN     NOT NULL DEFAULT TRUE,
                            assigned_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );
                        CREATE INDEX IF NOT EXISTS idx_staff_user ON %1$I.staff_assignments(user_id);
                        CREATE INDEX IF NOT EXISTS idx_staff_market ON %1$I.staff_assignments(market_id);

                        CREATE TABLE IF NOT EXISTS %1$I.categories (
                            id          SERIAL PRIMARY KEY,
                            name        VARCHAR(100) NOT NULL,
                            description TEXT,
                            parent_id   INT REFERENCES %1$I.categories(id) ON DELETE SET NULL,
                            is_active   BOOLEAN     NOT NULL DEFAULT TRUE,
                            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );

                        CREATE TABLE IF NOT EXISTS %1$I.suppliers (
                            id         SERIAL PRIMARY KEY,
                            name       VARCHAR(150) NOT NULL,
                            phone      VARCHAR(30),
                            email      VARCHAR(255),
                            address    TEXT,
                            is_active  BOOLEAN     NOT NULL DEFAULT TRUE,
                            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );

                        CREATE TABLE IF NOT EXISTS %1$I.products (
                            id               SERIAL PRIMARY KEY,
                            name             VARCHAR(200) NOT NULL,
                            description      TEXT,
                            barcode          VARCHAR(100) UNIQUE,
                            category_id      INT          REFERENCES %1$I.categories(id) ON DELETE SET NULL,
                            unit_price       DECIMAL(10,2) NOT NULL DEFAULT 0,
                            cost_price       DECIMAL(10,2) NOT NULL DEFAULT 0,
                            tax_rate         DECIMAL(5,2)  NOT NULL DEFAULT 0,
                            image_url        TEXT,
                            min_stock_alert  INT           NOT NULL DEFAULT 0,
                            is_active        BOOLEAN       NOT NULL DEFAULT TRUE,
                            created_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
                            search_vector    TSVECTOR GENERATED ALWAYS AS (
                                to_tsvector('simple', coalesce(name,'') || ' ' || coalesce(barcode,''))
                            ) STORED
                        );
                        CREATE INDEX IF NOT EXISTS idx_products_category ON %1$I.products(category_id);
                        CREATE INDEX IF NOT EXISTS idx_products_barcode ON %1$I.products(barcode);
                        CREATE INDEX IF NOT EXISTS idx_products_search ON %1$I.products USING GIN(search_vector);

                        CREATE TABLE IF NOT EXISTS %1$I.inventory (
                            id                 SERIAL PRIMARY KEY,
                            product_id         INT         NOT NULL REFERENCES %1$I.products(id) ON DELETE CASCADE,
                            market_id          INT         NOT NULL REFERENCES %1$I.markets(id) ON DELETE CASCADE,
                            department_id      INT         REFERENCES %1$I.departments(id) ON DELETE SET NULL,
                            quantity           INT         NOT NULL DEFAULT 0,
                            reserved_quantity  INT         NOT NULL DEFAULT 0,
                            last_updated_by    INT,
                            updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                            UNIQUE (product_id, market_id, department_id)
                        );
                        CREATE INDEX IF NOT EXISTS idx_inventory_market ON %1$I.inventory(market_id);
                        CREATE INDEX IF NOT EXISTS idx_inventory_product ON %1$I.inventory(product_id);

                        CREATE TABLE IF NOT EXISTS %1$I.purchases (
                            id                  SERIAL PRIMARY KEY,
                            supplier_id         INT           NOT NULL REFERENCES %1$I.suppliers(id),
                            market_id           INT           NOT NULL REFERENCES %1$I.markets(id) ON DELETE CASCADE,
                            created_by_user_id  INT           NOT NULL,
                            purchase_date       DATE          NOT NULL DEFAULT CURRENT_DATE,
                            status              VARCHAR(20)   NOT NULL DEFAULT 'Draft'
                                CHECK (status IN ('Pending', 'Draft', 'Ordered', 'PartiallyReceived', 'Received', 'Cancelled')),
                            total_amount        DECIMAL(12,2) NOT NULL DEFAULT 0,
                            notes               TEXT,
                            received_at         TIMESTAMPTZ,
                            created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW()
                        );
                        CREATE INDEX IF NOT EXISTS idx_purchases_supplier ON %1$I.purchases(supplier_id);
                        CREATE INDEX IF NOT EXISTS idx_purchases_market ON %1$I.purchases(market_id);
                        CREATE INDEX IF NOT EXISTS idx_purchases_status ON %1$I.purchases(status);

                        CREATE TABLE IF NOT EXISTS %1$I.purchase_items (
                            id          SERIAL PRIMARY KEY,
                            purchase_id INT           NOT NULL REFERENCES %1$I.purchases(id) ON DELETE CASCADE,
                            product_id  INT           NOT NULL REFERENCES %1$I.products(id),
                            quantity    INT           NOT NULL CHECK (quantity > 0),
                            received_quantity INT     NOT NULL DEFAULT 0 CHECK (received_quantity >= 0 AND received_quantity <= quantity),
                            unit_cost   DECIMAL(10,2) NOT NULL,
                            line_total  DECIMAL(12,2) GENERATED ALWAYS AS (quantity * unit_cost) STORED
                        );
                        CREATE INDEX IF NOT EXISTS idx_purchase_items_purchase ON %1$I.purchase_items(purchase_id);

                        CREATE TABLE IF NOT EXISTS %1$I.sales (
                            id                  SERIAL PRIMARY KEY,
                            market_id           INT           NOT NULL REFERENCES %1$I.markets(id) ON DELETE CASCADE,
                            created_by_user_id  INT           NOT NULL,
                            sale_date           DATE          NOT NULL DEFAULT CURRENT_DATE,
                            payment_method      VARCHAR(30)   NOT NULL DEFAULT 'Cash'
                                CHECK (payment_method IN ('Cash', 'Card', 'Transfer', 'Other')),
                            discount_amount     DECIMAL(10,2) NOT NULL DEFAULT 0,
                            total_amount        DECIMAL(12,2) NOT NULL DEFAULT 0,
                            notes               TEXT,
                            created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW()
                        );
                        CREATE INDEX IF NOT EXISTS idx_sales_market ON %1$I.sales(market_id);
                        CREATE INDEX IF NOT EXISTS idx_sales_date ON %1$I.sales(sale_date);

                        CREATE TABLE IF NOT EXISTS %1$I.sale_items (
                            id          SERIAL PRIMARY KEY,
                            sale_id     INT           NOT NULL REFERENCES %1$I.sales(id) ON DELETE CASCADE,
                            product_id  INT           NOT NULL REFERENCES %1$I.products(id),
                            quantity    INT           NOT NULL CHECK (quantity > 0),
                            unit_price  DECIMAL(10,2) NOT NULL,
                            line_total  DECIMAL(12,2) GENERATED ALWAYS AS (quantity * unit_price) STORED
                        );
                        CREATE INDEX IF NOT EXISTS idx_sale_items_sale ON %1$I.sale_items(sale_id);

                        CREATE TABLE IF NOT EXISTS %1$I.audit_logs (
                            id          BIGSERIAL PRIMARY KEY,
                            user_id     INT,
                            action      VARCHAR(100) NOT NULL,
                            table_name  VARCHAR(100),
                            record_id   INT,
                            old_data    JSONB,
                            new_data    JSONB,
                            ip_address  VARCHAR(45),
                            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );
                        CREATE INDEX IF NOT EXISTS idx_audit_user ON %1$I.audit_logs(user_id);
                        CREATE INDEX IF NOT EXISTS idx_audit_created ON %1$I.audit_logs(created_at);

                        CREATE TABLE IF NOT EXISTS %1$I.notifications (
                            id          BIGSERIAL PRIMARY KEY,
                            user_id     INT         NOT NULL,
                            type        VARCHAR(50) NOT NULL,
                            title       VARCHAR(200) NOT NULL,
                            body        TEXT,
                            is_read     BOOLEAN     NOT NULL DEFAULT FALSE,
                            sent_at     TIMESTAMPTZ,
                            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );
                        CREATE INDEX IF NOT EXISTS idx_notifications_user ON %1$I.notifications(user_id);
                        CREATE INDEX IF NOT EXISTS idx_notifications_unread ON %1$I.notifications(user_id) WHERE is_read = FALSE;

                        CREATE TABLE IF NOT EXISTS %1$I.ai_chat_sessions (
                            id          BIGSERIAL PRIMARY KEY,
                            user_id     INT         NOT NULL,
                            market_id   INT         REFERENCES %1$I.markets(id) ON DELETE SET NULL,
                            messages    JSONB       NOT NULL DEFAULT '[]'::JSONB,
                            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                            updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
                        );
                        CREATE INDEX IF NOT EXISTS idx_ai_sessions_user ON %1$I.ai_chat_sessions(user_id);
                    $tenant$, p_schema_name);
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

                DROP TRIGGER IF EXISTS trg_create_tenant_schema ON public.companies;

                CREATE TRIGGER trg_create_tenant_schema
                AFTER INSERT OR UPDATE OF schema_name ON public.companies
                FOR EACH ROW
                EXECUTE FUNCTION public.create_tenant_schema_for_company();

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT schema_name FROM public.companies LOOP
                        PERFORM public.create_tenant_schema(tenant.schema_name);
                    END LOOP;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_create_tenant_schema ON public.companies;
                DROP FUNCTION IF EXISTS public.create_tenant_schema_for_company();
                DROP FUNCTION IF EXISTS public.create_tenant_schema(text);
                """);
        }
    }
}
