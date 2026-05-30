using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524110000_AddDepartmentsManagePermissions")]
    public partial class AddDepartmentsManagePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) || '{
                    "departments:create": true,
                    "departments:update": true,
                    "departments:delete": true
                }'::jsonb
                WHERE name = 'CompanyAdmin';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb)
                    - 'departments:create'
                    - 'departments:update'
                    - 'departments:delete'
                WHERE name = 'CompanyAdmin';
                """);
        }
    }
}
