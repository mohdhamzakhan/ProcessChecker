using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EngineService.Migrations
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
                name: "PC_DASHBOARDS",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    OWNER_EMAIL = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    NAME = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    CONFIG_JSON = table.Column<string>(type: "CLOB", nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    UPDATED_AT = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_DASHBOARDS", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "PC_INSTANCES",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ProcessKey = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    ProcessName = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    ProcessVersion = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    Title = table.Column<string>(type: "NVARCHAR2(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "NVARCHAR2(20)", maxLength: 20, nullable: false),
                    StartedBy = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    SourceKey = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    DataJson = table.Column<string>(type: "CLOB", nullable: false),
                    DefinitionJson = table.Column<string>(type: "CLOB", nullable: false),
                    RowVer = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    OBSERVERS = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    REQUESTER = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_INSTANCES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PC_PARTICIPANTS",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    INSTANCE_ID = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    EMAIL = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    ROLE_NAME = table.Column<string>(type: "NVARCHAR2(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_PARTICIPANTS", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "PC_TRIGGER_SEEN",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ProcessKey = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    SourceKey = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    InstanceId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_TRIGGER_SEEN", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PC_STEPS",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    InstanceId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    StepKey = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    StepName = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    StepType = table.Column<string>(type: "NVARCHAR2(30)", maxLength: 30, nullable: false),
                    OrderNo = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    Status = table.Column<string>(type: "NVARCHAR2(20)", maxLength: 20, nullable: false),
                    OutputJson = table.Column<string>(type: "CLOB", nullable: true),
                    Error = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    ActionBy = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    ASSIGNEES = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    DUE_AT = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    REMIND_HOURS = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    LAST_REMINDER_AT = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    REMINDER_COUNT = table.Column<int>(type: "NUMBER(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_STEPS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PC_STEPS_PC_INSTANCES_InstanceId",
                        column: x => x.InstanceId,
                        principalSchema: "PROCESSCHECKER",
                        principalTable: "PC_INSTANCES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PC_APPROVALS",
                schema: "PROCESSCHECKER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    StepInstanceId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Approver = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Decision = table.Column<string>(type: "NVARCHAR2(20)", maxLength: 20, nullable: true),
                    Note = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TIMESTAMP(7)", nullable: true),
                    DECIDED_BY = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PC_APPROVALS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PC_APPROVALS_PC_STEPS_StepInstanceId",
                        column: x => x.StepInstanceId,
                        principalSchema: "PROCESSCHECKER",
                        principalTable: "PC_STEPS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PC_APPROVALS_Approver",
                schema: "PROCESSCHECKER",
                table: "PC_APPROVALS",
                column: "Approver");

            migrationBuilder.CreateIndex(
                name: "IX_PC_APPROVALS_StepInstanceId",
                schema: "PROCESSCHECKER",
                table: "PC_APPROVALS",
                column: "StepInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_PC_DASHBOARDS_OWNER_EMAIL",
                schema: "PROCESSCHECKER",
                table: "PC_DASHBOARDS",
                column: "OWNER_EMAIL");

            migrationBuilder.CreateIndex(
                name: "IX_PC_INSTANCES_ProcessKey",
                schema: "PROCESSCHECKER",
                table: "PC_INSTANCES",
                column: "ProcessKey");

            migrationBuilder.CreateIndex(
                name: "IX_PC_INSTANCES_Status",
                schema: "PROCESSCHECKER",
                table: "PC_INSTANCES",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PC_PARTICIPANTS_EMAIL_INSTANCE_ID",
                schema: "PROCESSCHECKER",
                table: "PC_PARTICIPANTS",
                columns: new[] { "EMAIL", "INSTANCE_ID" });

            migrationBuilder.CreateIndex(
                name: "IX_PC_STEPS_InstanceId",
                schema: "PROCESSCHECKER",
                table: "PC_STEPS",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_PC_STEPS_Status",
                schema: "PROCESSCHECKER",
                table: "PC_STEPS",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PC_TRIGGER_SEEN_ProcessKey_SourceKey",
                schema: "PROCESSCHECKER",
                table: "PC_TRIGGER_SEEN",
                columns: new[] { "ProcessKey", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PC_APPROVALS",
                schema: "PROCESSCHECKER");

            migrationBuilder.DropTable(
                name: "PC_DASHBOARDS",
                schema: "PROCESSCHECKER");

            migrationBuilder.DropTable(
                name: "PC_PARTICIPANTS",
                schema: "PROCESSCHECKER");

            migrationBuilder.DropTable(
                name: "PC_TRIGGER_SEEN",
                schema: "PROCESSCHECKER");

            migrationBuilder.DropTable(
                name: "PC_STEPS",
                schema: "PROCESSCHECKER");

            migrationBuilder.DropTable(
                name: "PC_INSTANCES",
                schema: "PROCESSCHECKER");
        }
    }
}
