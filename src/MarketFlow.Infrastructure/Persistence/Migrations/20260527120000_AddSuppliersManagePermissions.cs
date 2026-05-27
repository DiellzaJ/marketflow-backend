using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260527120000_AddSuppliersManagePermissions")]
    public partial class AddSuppliersManagePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) || '{
                    "suppliers:read": true,
                    "suppliers:create": true,
                    "suppliers:update": true,
                    "suppliers:delete": true
                }'::jsonb
                WHERE name = 'CompanyAdmin';

                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) || '{"suppliers:read": true}'::jsonb
                WHERE name = 'MainOperator';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb)
                    - 'suppliers:read'
                    - 'suppliers:create'
                    - 'suppliers:update'
                    - 'suppliers:delete'
                WHERE name = 'CompanyAdmin';

                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) - 'suppliers:read'
                WHERE name = 'MainOperator';
                """);
        }
    }
}
