using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSharpRefactoringAssistant.Migrations
{
    /// <inheritdoc />
    public partial class AddReasoningContentToMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReasoningContent",
                table: "Messages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReasoningContent",
                table: "Messages");
        }
    }
}
