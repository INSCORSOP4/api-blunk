/* ============================================================================
   003_bitacora.sql
   Bitacora de actividades de Soporte (reemplaza el Excel bitacora_soporte_YYYY.xlsx).
   Se ejecuta en la base SoporteKB (la misma de la base de conocimiento y de la auditoria
   de MICROSIP). Es idempotente.

   Primer grupo que usa esta tabla: 'actividades-soft' (la hoja "ACTIVIDADES SOFT" del Excel).
   El esquema es generico por diseno: una nueva bitacora de checklist diario (por ejemplo la
   hoja "RESPALDOS") puede reusar las mismas tablas con otro valor de Grupo, sin cambios de
   esquema; solo hace falta agregar el valor a BitacoraGrupos en el backend.

   Solo ASCII (sin acentos), igual que los scripts 001 y 002.
   Ejecutar con:  sqlcmd -S <servidor> -d SoporteKB -U <usuario> -P <clave> -C -I -i 003_bitacora.sql
   ============================================================================ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF SCHEMA_ID(N'bitacora') IS NULL EXEC(N'CREATE SCHEMA bitacora AUTHORIZATION dbo;');
GO

-- Catalogo de actividades a revisar. 'Grupo' separa cada bitacora (hoy solo 'actividades-soft').
IF OBJECT_ID(N'bitacora.Actividad', N'U') IS NULL
BEGIN
    CREATE TABLE bitacora.Actividad
    (
        ActividadId   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_bitacora_Actividad PRIMARY KEY,
        Grupo         varchar(40)   NOT NULL,
        Nombre        nvarchar(200) NOT NULL,
        Orden         int           NOT NULL CONSTRAINT DF_bitacora_Actividad_Orden DEFAULT (0),
        Activa        bit           NOT NULL CONSTRAINT DF_bitacora_Actividad_Activa DEFAULT (1),
        CreadoEn      datetime2(0)  NOT NULL CONSTRAINT DF_bitacora_Actividad_CreadoEn DEFAULT (SYSDATETIME()),
        CreadoPor     nvarchar(200) NOT NULL,
        ActualizadoEn datetime2(0)  NULL,
        ActualizadoPor nvarchar(200) NULL,
        CONSTRAINT UQ_bitacora_Actividad_Grupo_Nombre UNIQUE (Grupo, Nombre)
    );
    CREATE INDEX IX_bitacora_Actividad_Grupo ON bitacora.Actividad (Grupo, Activa, Orden);
    PRINT 'Tabla bitacora.Actividad creada.';
END
ELSE PRINT 'Tabla bitacora.Actividad ya existe.';
GO

-- Resultado de cada actividad en cada fecha. Una fila por (Fecha, ActividadId); se sobrescribe
-- si se corrige el mismo dia (ActualizadoPor/ActualizadoEn quedan del ultimo guardado).
IF OBJECT_ID(N'bitacora.ChecklistDiario', N'U') IS NULL
BEGIN
    CREATE TABLE bitacora.ChecklistDiario
    (
        Fecha          date          NOT NULL,
        ActividadId    int           NOT NULL,
        Estado         varchar(12)   NOT NULL,
        Observacion    nvarchar(1000) NULL,
        CreadoEn       datetime2(0)  NOT NULL CONSTRAINT DF_bitacora_Checklist_CreadoEn DEFAULT (SYSDATETIME()),
        CreadoPor      nvarchar(200) NOT NULL,
        ActualizadoEn  datetime2(0)  NULL,
        ActualizadoPor nvarchar(200) NULL,
        CONSTRAINT PK_bitacora_ChecklistDiario PRIMARY KEY (Fecha, ActividadId),
        CONSTRAINT FK_bitacora_Checklist_Actividad FOREIGN KEY (ActividadId)
            REFERENCES bitacora.Actividad (ActividadId),
        CONSTRAINT CK_bitacora_Checklist_Estado
            CHECK (Estado IN ('Ok', 'Advertencia', 'Falla', 'Critico', 'NoAplica')),
        -- Advertencia/Falla/Critico deben traer una observacion explicando que paso.
        CONSTRAINT CK_bitacora_Checklist_Observacion
            CHECK (Estado IN ('Ok', 'NoAplica') OR (Observacion IS NOT NULL AND LEN(LTRIM(RTRIM(Observacion))) > 0))
    );
    CREATE INDEX IX_bitacora_Checklist_Actividad ON bitacora.ChecklistDiario (ActividadId, Fecha);
    PRINT 'Tabla bitacora.ChecklistDiario creada.';
END
ELSE PRINT 'Tabla bitacora.ChecklistDiario ya existe.';
GO

-- Permisos: mismos roles que ya usan la base de conocimiento y la auditoria de MICROSIP.
IF DATABASE_PRINCIPAL_ID(N'kb_lector')   IS NOT NULL GRANT SELECT ON SCHEMA::bitacora TO kb_lector;
IF DATABASE_PRINCIPAL_ID(N'kb_escritor') IS NOT NULL GRANT SELECT, INSERT, UPDATE ON SCHEMA::bitacora TO kb_escritor;
GO

-- Catalogo inicial: las 20 actividades de la hoja "ACTIVIDADES SOFT" del Excel, en su mismo orden.
-- Si ya existen (por nombre) no se duplican.
MERGE bitacora.Actividad AS destino
USING (VALUES
    (1,  N'Reunion diaria con el equipo'),
    (2,  N'Revision de respaldos SQL Server (41 bases)'),
    (3,  N'Revision de respaldos SQL Server FORTIA (3)'),
    (4,  N'Revision de respaldos MICROSIP'),
    (5,  N'Revision de respaldos MERCALTOS'),
    (6,  N'Job Chesa'),
    (7,  N'Job Chesa Dia'),
    (8,  N'Job Calzamoda'),
    (9,  N'Job Caja Ahorro'),
    (10, N'Job Cincopinos'),
    (11, N'Job Depurar Logs'),
    (12, N'Job Insumos'),
    (13, N'Job Conciliador CFDI'),
    (14, N'Job Rio Vynil'),
    (15, N'Job Notificaciones Caja Ahorros'),
    (16, N'Checar archivos SIA Confianza, Chesa y Megamotriz 3.8'),
    (17, N'Encuestas Chesa 120 (91)'),
    (18, N'Validar envios recibos FORTIA'),
    (19, N'Revisar el espacio en HDD de los servidores 1.91 (120) y 3.8 (149)'),
    (20, N'Apagado de la 120')
) AS origen (Orden, Nombre)
ON destino.Grupo = N'actividades-soft' AND destino.Nombre = origen.Nombre
WHEN NOT MATCHED THEN
    INSERT (Grupo, Nombre, Orden, Activa, CreadoPor)
    VALUES (N'actividades-soft', origen.Nombre, origen.Orden, 1, N'migracion-excel');
GO

PRINT 'Listo.';
GO
