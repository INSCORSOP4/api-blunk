/* ============================================================================
   005_bitacora_camaras_depuracion.sql
   Dos bitacoras mas sobre el mismo esquema generico de 003_bitacora.sql (no crea tablas):

   - 'camaras': checklist DIARIO (como 'actividades-soft' y 'respaldos'). Al revisar el Excel
     "CAMARAS", resulto ser tambien una revision de todos los grupos todos los dias (no un
     log de incidentes suelto), asi que encaja igual en bitacora.Actividad / ChecklistDiario.
   - 'depuracion-respaldos': checklist MENSUAL (un registro por mes en vez de por dia; el backend
     no distingue granularidad, solo guarda una fecha -- el frontend siempre usa el dia 1 del mes).

   Ejecutar con:  sqlcmd -S <servidor> -d SoporteKB -U <usuario> -P <clave> -C -I -i 005_bitacora_camaras_depuracion.sql
   ============================================================================ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'bitacora.Actividad', N'U') IS NULL
    THROW 50000, N'bitacora.Actividad no existe. Ejecuta primero 003_bitacora.sql.', 1;
GO

MERGE bitacora.Actividad AS destino
USING (VALUES
    (N'camaras', 1, N'T Panama'),
    (N'camaras', 2, N'Deportivo'),
    (N'camaras', 3, N'T Merposur'),
    (N'camaras', 4, N'Prosur'),
    (N'camaras', 5, N'Departamentos'),
    (N'camaras', 6, N'Cascarita'),
    (N'camaras', 7, N'T Utrilla'),
    (N'depuracion-respaldos', 1, N'Micro Prosur (full)'),
    (N'depuracion-respaldos', 2, N'Mongo (full)'),
    (N'depuracion-respaldos', 3, N'SQL (full y diferencial)'),
    (N'depuracion-respaldos', 4, N'SQL FORTIA (full y diferencial)')
) AS origen (Grupo, Orden, Nombre)
ON destino.Grupo = origen.Grupo AND destino.Nombre = origen.Nombre
WHEN NOT MATCHED THEN
    INSERT (Grupo, Nombre, Orden, Activa, CreadoPor)
    VALUES (origen.Grupo, origen.Nombre, origen.Orden, 1, N'migracion-excel');
GO

PRINT 'Listo.';
GO
