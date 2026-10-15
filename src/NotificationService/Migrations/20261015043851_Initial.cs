using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Migrations
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
                name: "PC_NOTIFICATIONS",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ToList = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: false),
                    Subject = table.Column<string>(type: "NVARCHAR2(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "CLOB", nullable: false),
                    IsHtml = table.Column<bool>(type: "NUMBER(1)", nullable: false),
                    Status = table.Column<string>(type: "NVARCHAR2(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    Attempts = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    ATTACHMENTS = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_NOTIFICATIONS", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PC_NOTIFICATIONS_CreatedAt",
                schema: "PROCESSCHECKER",
                table: "PC_NOTIFICATIONS",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PC_NOTIFICATIONS_Status",
                schema: "PROCESSCHECKER",
                table: "PC_NOTIFICATIONS",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PC_NOTIFICATIONS",
                schema: "PROCESSCHECKER");
        }
    }
}
