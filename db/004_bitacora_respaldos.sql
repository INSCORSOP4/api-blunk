/* ============================================================================
   004_bitacora_respaldos.sql
   Segunda bitacora de checklist diario (hoja "RESPALDOS (DIA ANTERIOR)" del Excel).
   No crea tablas: bitacora.Actividad y bitacora.ChecklistDiario (003_bitacora.sql) ya son genericas;
   esto solo siembra el catalogo del grupo 'respaldos'. Es idempotente.

   Ejecutar con:  sqlcmd -S <servidor> -d SoporteKB -U <usuario> -P <clave> -C -I -i 004_bitacora_respaldos.sql
   ============================================================================ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'bitacora.Actividad', N'U') IS NULL
    THROW 50000, N'bitacora.Actividad no existe. Ejecuta primero 003_bitacora.sql.', 1;
GO

MERGE bitacora.Actividad AS destino
USING (VALUES
    (1, N'TOR Mercal'),
    (2, N'Micro Prosur'),
    (3, N'Mongo'),
    (4, N'SQL (41 bases)'),
    (5, N'SQL (3 FORTIA)')
) AS origen (Orden, Nombre)
ON destino.Grupo = N'respaldos' AND destino.Nombre = origen.Nombre
WHEN NOT MATCHED THEN
    INSERT (Grupo, Nombre, Orden, Activa, CreadoPor)
    VALUES (N'respaldos', origen.Nombre, origen.Orden, 1, N'migracion-excel');
GO

PRINT 'Listo.';
GO
