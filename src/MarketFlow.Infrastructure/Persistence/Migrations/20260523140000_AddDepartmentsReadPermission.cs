using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260523140000_AddDepartmentsReadPermission")]
    public partial class AddDepartmentsReadPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) || '{"departments:read": true}'::jsonb
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
                SET permissions = COALESCE(permissions, '{}'::jsonb) - 'departments:read'
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
