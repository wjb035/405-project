using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGEmuBackend.Migrations
{
    /// <inheritdoc />
    public partial class UpdateFriendModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Friends_Users_FriendId",
                table: "Friends");

            migrationBuilder.DropForeignKey(
                name: "FK_Friends_Users_UserId",
                table: "Friends");

            migrationBuilder.RenameColumn(
                name: "FriendId",
                table: "Friends",
                newName: "ReceiverId");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "Friends",
                newName: "SenderId");

            migrationBuilder.RenameIndex(
                name: "IX_Friends_FriendId",
                table: "Friends",
                newName: "IX_Friends_ReceiverId");

            migrationBuilder.AddForeignKey(
                name: "FK_Friends_Users_ReceiverId",
                table: "Friends",
                column: "ReceiverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Friends_Users_SenderId",
                table: "Friends",
                column: "SenderId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Friends_Users_ReceiverId",
                table: "Friends");

            migrationBuilder.DropForeignKey(
                name: "FK_Friends_Users_SenderId",
                table: "Friends");

            migrationBuilder.RenameColumn(
                name: "ReceiverId",
                table: "Friends",
                newName: "FriendId");

            migrationBuilder.RenameColumn(
                name: "SenderId",
                table: "Friends",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Friends_ReceiverId",
                table: "Friends",
                newName: "IX_Friends_FriendId");

            migrationBuilder.AddForeignKey(
                name: "FK_Friends_Users_FriendId",
                table: "Friends",
                column: "FriendId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Friends_Users_UserId",
                table: "Friends",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
