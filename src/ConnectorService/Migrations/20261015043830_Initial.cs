using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConnectorService.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "PROCESSCHECKER");

            migrationBuilder.CreateTable(
                name: "PC_CONNECTIONS",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    CONN_TYPE = table.Column<string>(type: "NVARCHAR2(30)", maxLength: 30, nullable: false),
                    ProtectedConnectionString = table.Column<string>(type: "CLOB", nullable: true),
                    FilePath = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    OptionsJson = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    DEFAULT_SCHEMA = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: true),
                    ALLOWED_SCHEMAS = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_CONNECTIONS", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PC_CONNECTIONS_Name",
                schema: "PROCESSCHECKER",
                table: "PC_CONNECTIONS",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PC_CONNECTIONS",
                schema: "PROCESSCHECKER");
        }
    }
}
