BEGIN TRANSACTION;
ALTER TABLE "Pedidos" ADD "ProcessadoEmUtc" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261105185618_Processamento', '10.0.13');

COMMIT;

