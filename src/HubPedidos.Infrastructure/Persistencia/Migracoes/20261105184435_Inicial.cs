using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HubPedidos.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pedidos",
                columns: table => new
                {
                    Numero = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClienteId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Regiao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Prioridade = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Categoria = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MotivoDaCategoria = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Processador = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    PrazoEstimadoEmDias = table.Column<int>(type: "INTEGER", nullable: false),
                    Situacao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValorTotalCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Entrega_Cep = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Entrega_Cidade = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Entrega_Logradouro = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Entrega_Uf = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pedidos", x => x.Numero);
                });

            migrationBuilder.CreateTable(
                name: "ItensPedido",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PedidoNumero = table.Column<long>(type: "INTEGER", nullable: false),
                    Posicao = table.Column<int>(type: "INTEGER", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Quantidade = table.Column<int>(type: "INTEGER", nullable: false),
                    PrecoUnitarioCentavos = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensPedido", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensPedido_Pedidos_PedidoNumero",
                        column: x => x.PedidoNumero,
                        principalTable: "Pedidos",
                        principalColumn: "Numero",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItensPedido_PedidoNumero_Posicao",
                table: "ItensPedido",
                columns: new[] { "PedidoNumero", "Posicao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Categoria",
                table: "Pedidos",
                column: "Categoria");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_ClienteId",
                table: "Pedidos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CriadoEm_Numero",
                table: "Pedidos",
                columns: new[] { "CriadoEmUtc", "Numero" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Id",
                table: "Pedidos",
                column: "Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItensPedido");

            migrationBuilder.DropTable(
                name: "Pedidos");
        }
    }
}
