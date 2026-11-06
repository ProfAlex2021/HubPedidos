using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HubPedidos.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Processamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessadoEmUtc",
                table: "Pedidos",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessadoEmUtc",
                table: "Pedidos");
        }
    }
}
