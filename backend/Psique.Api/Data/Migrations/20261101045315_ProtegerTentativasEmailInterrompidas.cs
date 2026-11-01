using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psique.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtegerTentativasEmailInterrompidas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EnvioEmailIniciadoEm",
                table: "Lembretes",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnvioEmailIniciadoEm",
                table: "Lembretes");
        }
    }
}
