using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260521190000_DefineInventoryPermissionMatrix")]
    public partial class DefineInventoryPermissionMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = '{
                    "company": true,
                    "companies:read": true,
                    "companies:create": true,
                    "companies:update": true,
                    "companies:delete": true,
                    "users:read": true,
                    "users:create": true,
                    "users:update": true,
                    "users:delete": true
                }'::jsonb
                WHERE name = 'RootAdmin';

                UPDATE public.roles
                SET permissions = permissions
                    - 'inventory:update'
                    || '{
                        "inventory:create": true,
                        "inventory:read": true,
                        "stock:update": true,
                        "stock:adjust": true,
                        "inventory:delete": true,
                        "inventory-movements:read": true,
                        "stock:transfer": true
                    }'::jsonb
                WHERE name IN ('CompanyAdmin', 'MainOperator');

                UPDATE public.roles
                SET permissions = (permissions
                    - 'inventory:create'
                    - 'inventory:update'
                    - 'inventory:delete')
                    || '{
                        "inventory:read": true,
                        "stock:update": true,
                        "stock:adjust": true,
                        "inventory-movements:read": true,
                        "stock:transfer": true
                    }'::jsonb
                WHERE name = 'DepartmentManager';

                UPDATE public.roles
                SET permissions = (permissions - 'inventory:update')
                    || '{
                        "inventory:read": true,
                        "stock:update": true,
                        "stock:adjust": true,
                        "inventory-movements:read": true
                    }'::jsonb
                WHERE name = 'InventoryEmployee';

                UPDATE public.roles
                SET permissions = permissions - 'inventory:update'
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
                SET permissions = (permissions
                    - 'stock:update'
                    - 'stock:adjust'
                    - 'inventory-movements:read'
                    - 'stock:transfer')
                    || '{
                        "inventory:create": true,
                        "inventory:read": true,
                        "inventory:update": true,
                        "inventory:delete": true
                    }'::jsonb
                WHERE name IN ('CompanyAdmin', 'MainOperator');

                UPDATE public.roles
                SET permissions = (permissions
                    - 'stock:update'
                    - 'stock:adjust'
                    - 'inventory-movements:read'
                    - 'stock:transfer')
                    || '{
                        "inventory:create": true,
                        "inventory:read": true,
                        "inventory:update": true,
                        "inventory:delete": true
                    }'::jsonb
                WHERE name = 'DepartmentManager';

                UPDATE public.roles
                SET permissions = (permissions
                    - 'stock:update'
                    - 'stock:adjust'
                    - 'inventory-movements:read')
                    || '{
                        "inventory:read": true,
                        "inventory:update": true
                    }'::jsonb
                WHERE name = 'InventoryEmployee';

                UPDATE public.roles
                SET permissions = permissions || '{"inventory:update": true}'::jsonb
                WHERE name = 'Seller';
                """);
        }
    }
}
