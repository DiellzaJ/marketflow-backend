using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260523150000_AddMarketsManagePermissions")]
    public partial class AddMarketsManagePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.roles
                SET permissions = COALESCE(permissions, '{}'::jsonb) || '{
                    "markets:create": true,
                    "markets:update": true,
                    "markets:delete": true
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
                    - 'markets:create'
                    - 'markets:update'
                    - 'markets:delete'
                WHERE name = 'CompanyAdmin';
                """);
        }
    }
}
