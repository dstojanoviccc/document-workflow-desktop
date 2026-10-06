using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentWorkflow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImmutableVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BaseVersion",
                table: "Versions",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseVersion",
                table: "Versions");
        }
    }
}
