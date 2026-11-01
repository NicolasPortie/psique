using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psique.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliarConfirmacoesWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MensagemExternaId",
                table: "EventosBridgeWhatsApp",
                type: "nvarchar(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusMensagem",
                table: "EventosBridgeWhatsApp",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosBridgeWhatsApp_ContaPsicologoId_MensagemExternaId",
                table: "EventosBridgeWhatsApp",
                columns: new[] { "ContaPsicologoId", "MensagemExternaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EventosBridgeWhatsApp_ContaPsicologoId_MensagemExternaId",
                table: "EventosBridgeWhatsApp");

            migrationBuilder.DropColumn(
                name: "MensagemExternaId",
                table: "EventosBridgeWhatsApp");

            migrationBuilder.DropColumn(
                name: "StatusMensagem",
                table: "EventosBridgeWhatsApp");
        }
    }
}
