CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "Pedidos" (
    "Numero" INTEGER NOT NULL CONSTRAINT "PK_Pedidos" PRIMARY KEY AUTOINCREMENT,
    "Id" TEXT NOT NULL,
    "ClienteId" TEXT NOT NULL,
    "Regiao" TEXT NOT NULL,
    "Prioridade" TEXT NOT NULL,
    "Categoria" TEXT NOT NULL,
    "MotivoDaCategoria" TEXT NOT NULL,
    "Processador" TEXT NOT NULL,
    "PrazoEstimadoEmDias" INTEGER NOT NULL,
    "Situacao" TEXT NOT NULL,
    "CriadoEmUtc" TEXT NOT NULL,
    "ValorTotalCentavos" INTEGER NOT NULL,
    "Entrega_Cep" TEXT NULL,
    "Entrega_Cidade" TEXT NULL,
    "Entrega_Logradouro" TEXT NULL,
    "Entrega_Uf" TEXT NULL
);

CREATE TABLE "ItensPedido" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ItensPedido" PRIMARY KEY AUTOINCREMENT,
    "PedidoNumero" INTEGER NOT NULL,
    "Posicao" INTEGER NOT NULL,
    "Sku" TEXT NOT NULL,
    "Quantidade" INTEGER NOT NULL,
    "PrecoUnitarioCentavos" INTEGER NOT NULL,
    CONSTRAINT "FK_ItensPedido_Pedidos_PedidoNumero" FOREIGN KEY ("PedidoNumero") REFERENCES "Pedidos" ("Numero") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_ItensPedido_PedidoNumero_Posicao" ON "ItensPedido" ("PedidoNumero", "Posicao");

CREATE INDEX "IX_Pedidos_Categoria" ON "Pedidos" ("Categoria");

CREATE INDEX "IX_Pedidos_ClienteId" ON "Pedidos" ("ClienteId");

CREATE INDEX "IX_Pedidos_CriadoEm_Numero" ON "Pedidos" ("CriadoEmUtc", "Numero");

CREATE UNIQUE INDEX "IX_Pedidos_Id" ON "Pedidos" ("Id");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261105184435_Inicial', '10.0.13');

COMMIT;

