using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentWorkflow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    BaseVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    CheckoutAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    DeduplicationKey = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEvents_DocumentId_DeduplicationKey",
                table: "WorkflowEvents",
                columns: new[] { "DocumentId", "DeduplicationKey" },
                unique: true);
            migrationBuilder.Sql("CREATE TRIGGER WorkflowEvents_NoUpdate BEFORE UPDATE ON WorkflowEvents BEGIN SELECT RAISE(ABORT, 'Workflow events are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER WorkflowEvents_NoDelete BEFORE DELETE ON WorkflowEvents BEGIN SELECT RAISE(ABORT, 'Workflow events are append-only'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowEvents");
        }
    }
}
