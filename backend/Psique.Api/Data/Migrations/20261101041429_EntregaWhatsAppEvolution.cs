using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psique.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EntregaWhatsAppEvolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EnvioIniciadoEm",
                table: "MensagensWhatsApp",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LembreteId",
                table: "MensagensWhatsApp",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instancia",
                table: "IntegracoesWhatsApp",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MensagensWhatsApp_ContaPsicologoId_LembreteId",
                table: "MensagensWhatsApp",
                columns: new[] { "ContaPsicologoId", "LembreteId" },
                unique: true,
                filter: "[LembreteId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_MensagensWhatsApp_Lembretes_ContaPsicologoId_LembreteId",
                table: "MensagensWhatsApp",
                columns: new[] { "ContaPsicologoId", "LembreteId" },
                principalTable: "Lembretes",
                principalColumns: new[] { "ContaPsicologoId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MensagensWhatsApp_Lembretes_ContaPsicologoId_LembreteId",
                table: "MensagensWhatsApp");

            migrationBuilder.DropIndex(
                name: "IX_MensagensWhatsApp_ContaPsicologoId_LembreteId",
                table: "MensagensWhatsApp");

            migrationBuilder.DropColumn(
                name: "EnvioIniciadoEm",
                table: "MensagensWhatsApp");

            migrationBuilder.DropColumn(
                name: "LembreteId",
                table: "MensagensWhatsApp");

            migrationBuilder.DropColumn(
                name: "Instancia",
                table: "IntegracoesWhatsApp");
        }
    }
}
