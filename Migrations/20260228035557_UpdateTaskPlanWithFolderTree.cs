using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSharpRefactoringAssistant.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTaskPlanWithFolderTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FoldersJson",
                table: "PlannedTasks");

            migrationBuilder.AddColumn<string>(
                name: "FolderStructureJson",
                table: "PlannedTasks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FolderStructureJson",
                table: "PlannedTasks");

            migrationBuilder.AddColumn<string>(
                name: "FoldersJson",
                table: "PlannedTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
