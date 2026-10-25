/* ============================================================================
   006_archivos_confianza.sql
   Catalogo y revision diaria de los archivos de datos que llegan por red (SIA Confianza y
   similares) y que SQL Server consume para mostrarlos en los sistemas internos. Reemplaza al
   script notify.py que corria en el 10.0.3.8 y solo mandaba un correo: ahora el resultado de
   cada revision se guarda en SoporteKB para verse dentro del sistema.
   Se ejecuta en la base SoporteKB (la misma de la base de conocimiento, la auditoria de
   MICROSIP y la bitacora). Es idempotente.

   Esquema generico por diseno, igual que bitacora: un catalogo (ArchivoEsperado) y una tabla
   de resultados por dia (RevisionArchivo), con PK compuesta (Fecha, ArchivoId) para que
   correr la revision dos veces el mismo dia corrija el resultado en vez de duplicarlo.

   Solo ASCII (sin acentos), igual que los scripts 001, 002 y 003.
   Ejecutar con:  sqlcmd -S <servidor> -d SoporteKB -U <usuario> -P <clave> -C -I -i 006_archivos_confianza.sql
   ============================================================================ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF SCHEMA_ID(N'archivos') IS NULL EXEC(N'CREATE SCHEMA archivos AUTHORIZATION dbo;');
GO

-- Catalogo de archivos esperados: la ruta de red (UNC) y el nombre exacto del archivo.
IF OBJECT_ID(N'archivos.ArchivoEsperado', N'U') IS NULL
BEGIN
    CREATE TABLE archivos.ArchivoEsperado
    (
        ArchivoId     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_archivos_ArchivoEsperado PRIMARY KEY,
        Ruta          nvarchar(260) NOT NULL,
        NombreArchivo nvarchar(260) NOT NULL,
        Activo        bit           NOT NULL CONSTRAINT DF_archivos_ArchivoEsperado_Activo DEFAULT (1),
        CreadoEn      datetime2(0)  NOT NULL CONSTRAINT DF_archivos_ArchivoEsperado_CreadoEn DEFAULT (SYSDATETIME()),
        CreadoPor     nvarchar(200) NOT NULL,
        ActualizadoEn datetime2(0)  NULL,
        ActualizadoPor nvarchar(200) NULL,
        CONSTRAINT UQ_archivos_ArchivoEsperado_Ruta_Nombre UNIQUE (Ruta, NombreArchivo)
    );
    CREATE INDEX IX_archivos_ArchivoEsperado_Activo ON archivos.ArchivoEsperado (Activo);
    PRINT 'Tabla archivos.ArchivoEsperado creada.';
END
ELSE PRINT 'Tabla archivos.ArchivoEsperado ya existe.';
GO

-- Resultado de cada revision por dia. Una fila por (Fecha, ArchivoId); si el script se vuelve
-- a correr el mismo dia, se corrige el resultado (no se duplica ni se borra el historico previo).
IF OBJECT_ID(N'archivos.RevisionArchivo', N'U') IS NULL
BEGIN
    CREATE TABLE archivos.RevisionArchivo
    (
        Fecha              date          NOT NULL,
        ArchivoId          int           NOT NULL,
        Estado             varchar(14)   NOT NULL,
        UltimaModificacion datetime2(0)  NULL,
        Detalle            nvarchar(400) NULL,
        RevisadoEn         datetime2(0)  NOT NULL CONSTRAINT DF_archivos_RevisionArchivo_RevisadoEn DEFAULT (SYSDATETIME()),
        RevisadoPor        nvarchar(200) NOT NULL,
        CONSTRAINT PK_archivos_RevisionArchivo PRIMARY KEY (Fecha, ArchivoId),
        CONSTRAINT FK_archivos_RevisionArchivo_ArchivoEsperado FOREIGN KEY (ArchivoId)
            REFERENCES archivos.ArchivoEsperado (ArchivoId),
        -- Actualizado: se modifico hoy. Desactualizado: existe pero su ultima modificacion es de
        -- un dia anterior. Faltante: no se encontro en la ruta. Error: no se pudo revisar (la ruta
        -- de red no respondio, por ejemplo), para no confundirlo con un archivo realmente faltante.
        CONSTRAINT CK_archivos_RevisionArchivo_Estado
            CHECK (Estado IN ('Actualizado', 'Desactualizado', 'Faltante', 'Error'))
    );
    CREATE INDEX IX_archivos_RevisionArchivo_Archivo ON archivos.RevisionArchivo (ArchivoId, Fecha DESC);
    PRINT 'Tabla archivos.RevisionArchivo creada.';
END
ELSE PRINT 'Tabla archivos.RevisionArchivo ya existe.';
GO

-- Permisos: mismos roles que ya usan la base de conocimiento, la auditoria de MICROSIP y la bitacora.
IF DATABASE_PRINCIPAL_ID(N'kb_lector')   IS NOT NULL GRANT SELECT ON SCHEMA::archivos TO kb_lector;
IF DATABASE_PRINCIPAL_ID(N'kb_escritor') IS NOT NULL GRANT SELECT, INSERT, UPDATE ON SCHEMA::archivos TO kb_escritor;
GO

-- Catalogo inicial: lo que hoy revisa notify.py, con \\10.8.0.17 reemplazado por la IP nueva
-- \\10.50.0.31 (segun lo confirmado). Si ya existen (por Ruta+NombreArchivo) no se duplican.
MERGE archivos.ArchivoEsperado AS destino
USING (VALUES
    (N'\\172.26.12.160\Publico', N'Repbi12.txt'),
    (N'\\10.50.0.31\pasa',       N'Repbi12.txt'),
    (N'\\10.8.0.32\pasa',        N'Repbi12.txt'),
    (N'\\172.26.12.160\Publico', N'repbi17.csv'),
    (N'\\172.26.12.160\Publico', N'repbi20.txt'),
    (N'\\172.26.12.160\Publico', N'repbi19.txt'),
    (N'\\172.26.12.160\Publico', N'repbi18.txt'),
    (N'\\172.26.12.160\Publico', N'olap301.txt'),
    (N'\\10.50.0.31\pasa',       N'repbi17.txt'),
    (N'\\10.50.0.31\pasa',       N'repbi18.txt'),
    (N'\\10.50.0.31\pasa',       N'olap302.txt'),
    (N'\\10.8.0.32\pasa',        N'repbi17.txt'),
    (N'\\10.8.0.32\pasa',        N'repbi18.txt'),
    (N'\\10.8.0.32\pasa',        N'olap303.txt'),
    (N'\\172.26.12.160\Publico', N'repbi21.csv'),
    (N'\\10.8.0.32\pasa',        N'repbi21.csv'),
    (N'\\10.50.0.31\pasa',       N'repbi21.csv'),
    (N'\\172.26.12.160\Publico', N'repbi22.csv'),
    (N'\\10.50.0.31\pasa',       N'repbi22.csv')
) AS origen (Ruta, NombreArchivo)
ON destino.Ruta = origen.Ruta AND destino.NombreArchivo = origen.NombreArchivo
WHEN NOT MATCHED THEN
    INSERT (Ruta, NombreArchivo, Activo, CreadoPor)
    VALUES (origen.Ruta, origen.NombreArchivo, 1, N'migracion-notify');
GO

PRINT 'Listo.';
GO
