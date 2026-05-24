using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524170000_AddDepartmentManagerSalesReadPermission")]
    public partial class AddDepartmentManagerSalesReadPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) || '{"sales:read": true}'::jsonb
                WHERE name = 'DepartmentManager';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) - 'sales:read'
                WHERE name = 'DepartmentManager';
                """);
        }
    }
}
