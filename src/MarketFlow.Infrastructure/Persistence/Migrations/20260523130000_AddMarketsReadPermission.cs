using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260523130000_AddMarketsReadPermission")]
    public partial class AddMarketsReadPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) || '{"markets:read": true}'::jsonb
                WHERE name IN (
                    'CompanyAdmin',
                    'MainOperator',
                    'DepartmentManager',
                    'InventoryEmployee',
                    'Seller'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) - 'markets:read'
                WHERE name IN (
                    'CompanyAdmin',
                    'MainOperator',
                    'DepartmentManager',
                    'InventoryEmployee',
                    'Seller'
                );
                """);
        }
    }
}
