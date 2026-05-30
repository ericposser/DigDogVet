using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigDog.Migrations
{
    public partial class AdicionarIndices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Painel e agendamentos — filtros mais frequentes do sistema
            migrationBuilder.CreateIndex(
                name: "IX_BanhoTosa_IdEmpresa_DataHora",
                table: "BanhoTosa",
                columns: new[] { "IdEmpresa", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_Consulta_IdEmpresa_DataHora",
                table: "Consulta",
                columns: new[] { "IdEmpresa", "DataHora" });

            // Tokens públicos — consultados sem autenticação, precisam ser rápidos
            migrationBuilder.CreateIndex(
                name: "IX_KanbanToken_TokenHash",
                table: "KanbanToken",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_CarteiraToken_TokenHash",
                table: "CarteiraToken",
                column: "TokenHash");

            // Logs — filtrados por empresa e ordenados por data na tela de auditoria
            migrationBuilder.CreateIndex(
                name: "IX_Log_IdEmpresa_DataHora",
                table: "Log",
                columns: new[] { "IdEmpresa", "DataHora" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex("IX_BanhoTosa_IdEmpresa_DataHora",  "BanhoTosa");
            migrationBuilder.DropIndex("IX_Consulta_IdEmpresa_DataHora",   "Consulta");
            migrationBuilder.DropIndex("IX_KanbanToken_TokenHash",         "KanbanToken");
            migrationBuilder.DropIndex("IX_CarteiraToken_TokenHash",       "CarteiraToken");
            migrationBuilder.DropIndex("IX_Log_IdEmpresa_DataHora",        "Log");
        }
    }
}