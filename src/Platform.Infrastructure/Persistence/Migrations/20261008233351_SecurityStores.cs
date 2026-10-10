using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credential_nonces",
                schema: "app",
                columns: table => new
                {
                    nonce_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credential_nonces", x => x.nonce_hash);
                })
                .Annotation("Npgsql:UnloggedTable", true);

            migrationBuilder.CreateTable(
                name: "envelope_keys",
                schema: "identity",
                columns: table => new
                {
                    kid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    public_key_pem = table.Column<string>(type: "text", nullable: false),
                    private_key_enc = table.Column<byte[]>(type: "bytea", nullable: false),
                    nonce_mac_key_enc = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envelope_keys", x => x.kid);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credential_nonces_expiry",
                schema: "app",
                table: "credential_nonces",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_envelope_keys_created",
                schema: "identity",
                table: "envelope_keys",
                column: "created_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credential_nonces",
                schema: "app");

            migrationBuilder.DropTable(
                name: "envelope_keys",
                schema: "identity");
        }
    }
}
