using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentService.Migrations
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
                name: "PC_DOCUMENTS",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    FileName = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    StoredPath = table.Column<string>(type: "NVARCHAR2(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "NVARCHAR2(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    Sha256 = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    InstanceId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    StepKey = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: true),
                    UploadedBy = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_DOCUMENTS", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PC_DOCUMENTS_InstanceId",
                schema: "PROCESSCHECKER",
                table: "PC_DOCUMENTS",
                column: "InstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PC_DOCUMENTS",
                schema: "PROCESSCHECKER");
        }
    }
}
