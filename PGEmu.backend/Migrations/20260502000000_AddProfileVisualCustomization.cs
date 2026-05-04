using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PGEmuBackend.Data;

#nullable disable

namespace PGEmuBackend.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260502000000_AddProfileVisualCustomization")]
    public partial class AddProfileVisualCustomization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarFrame",
                table: "UserProfiles",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "rounded")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProfileAccent",
                table: "UserProfiles",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "sky")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProfileBackground",
                table: "UserProfiles",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "nebula")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarFrame",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ProfileAccent",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ProfileBackground",
                table: "UserProfiles");
        }
    }
}
