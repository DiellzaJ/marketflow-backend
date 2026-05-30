using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MarketFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevokedAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "revoked_access_tokens",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    token_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revoked_access_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_revoked_access_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_revoked_access_tokens_expires_at",
                schema: "public",
                table: "revoked_access_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_revoked_access_tokens_token_id",
                schema: "public",
                table: "revoked_access_tokens",
                column: "token_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_revoked_access_tokens_user_id",
                schema: "public",
                table: "revoked_access_tokens",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revoked_access_tokens",
                schema: "public");
        }
    }
}
