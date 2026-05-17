using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260517120000_UpdateRolePermissionsToActionStyle")]
    public partial class UpdateRolePermissionsToActionStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = '{"all": true}'::jsonb
                WHERE name = 'RootAdmin';

                UPDATE public.roles
                SET permissions = '{
                    "users:read": true,
                    "users:create": true,
                    "users:update": true,
                    "users:delete": true,
                    "products:read": true,
                    "products:create": true,
                    "products:update": true,
                    "products:delete": true,
                    "sales:read": true,
                    "sales:create": true,
                    "inventory:read": true,
                    "inventory:update": true,
                    "purchases:read": true,
                    "purchases:create": true,
                    "purchases:update": true
                }'::jsonb
                WHERE name = 'CompanyAdmin';

                UPDATE public.roles
                SET permissions = '{
                    "products:read": true,
                    "products:create": true,
                    "products:update": true,
                    "products:delete": true,
                    "sales:read": true,
                    "sales:create": true,
                    "inventory:read": true,
                    "inventory:update": true,
                    "purchases:read": true,
                    "purchases:create": true,
                    "purchases:update": true
                }'::jsonb
                WHERE name = 'MainOperator';

                UPDATE public.roles
                SET permissions = '{
                    "products:read": true,
                    "inventory:read": true,
                    "inventory:update": true
                }'::jsonb
                WHERE name IN ('DepartmentManager', 'InventoryEmployee');

                UPDATE public.roles
                SET permissions = '{
                    "products:read": true,
                    "sales:create": true,
                    "inventory:read": true,
                    "inventory:update": true
                }'::jsonb
                WHERE name = 'Seller';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = '{"all": true}'::jsonb
                WHERE name = 'RootAdmin';

                UPDATE public.roles
                SET permissions = '{"company": true, "markets": true, "users": true}'::jsonb
                WHERE name = 'CompanyAdmin';

                UPDATE public.roles
                SET permissions = '{"market": true, "products": true, "purchases": true, "sales": true}'::jsonb
                WHERE name = 'MainOperator';

                UPDATE public.roles
                SET permissions = '{"department": true, "inventory": true, "employees": true}'::jsonb
                WHERE name = 'DepartmentManager';

                UPDATE public.roles
                SET permissions = '{"inventory": "read-update"}'::jsonb
                WHERE name = 'InventoryEmployee';

                UPDATE public.roles
                SET permissions = '{"sales": true, "inventory": "update"}'::jsonb
                WHERE name = 'Seller';
                """);
        }
    }
}
