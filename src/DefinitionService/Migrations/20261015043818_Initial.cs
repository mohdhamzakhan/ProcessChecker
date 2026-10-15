using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DefinitionService.Migrations
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
                name: "PC_DEFINITIONS",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    DEF_KEY = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    IsActive = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    JsonBody = table.Column<string>(type: "CLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_DEFINITIONS", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PC_DEFINITIONS_DEF_KEY_Version",
                schema: "PROCESSCHECKER",
                table: "PC_DEFINITIONS",
                columns: new[] { "DEF_KEY", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PC_DEFINITIONS",
                schema: "PROCESSCHECKER");
        }
    }
}
