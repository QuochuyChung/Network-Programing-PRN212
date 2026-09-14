using ChatServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatServer.Data.Migrations;

[DbContext(typeof(ChatDbContext))]
[Migration("20260914160000_AddUserAndGroupConstraints")]
public sealed class AddUserAndGroupConstraints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "Groups",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "Username",
            table: "Users",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.CreateIndex(
            name: "IX_Users_Username",
            table: "Users",
            column: "Username",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Users_Username",
            table: "Users");

        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "Groups",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100);

        migrationBuilder.AlterColumn<string>(
            name: "Username",
            table: "Users",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(50)",
            oldMaxLength: 50);
    }
}
