/* =============================================================================
   SoporteKB - Base de conocimiento de Soporte Software
   Motor    : SQL Server 2019 (15.x) Standard, con Full-Text Search instalado
   Busqueda : texto completo en espanol (LCID 3082), sin distinguir acentos
   Es re-ejecutable: cada objeto se crea solo si todavia no existe.
   Requiere : permiso CREATE DATABASE (sysadmin o dbcreator) para el primer bloque.

   No crea logins ni contrasenas. Al final hay una seccion COMENTADA con la plantilla
   para crear los usuarios de la API; reemplaza las contrasenas antes de usarla.
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------------------------- 1. Base de datos */
IF DB_ID(N'SoporteKB') IS NULL
    CREATE DATABASE SoporteKB COLLATE Modern_Spanish_CI_AS;
GO

ALTER DATABASE SoporteKB SET COMPATIBILITY_LEVEL = 150;
-- Las lecturas de la consola no bloquean a las escrituras de una importacion en curso.
ALTER DATABASE SoporteKB SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO

USE SoporteKB;
GO

IF SCHEMA_ID(N'kb') IS NULL
    EXEC (N'CREATE SCHEMA kb AUTHORIZATION dbo');
GO

/* ------------------------------------------------------------ 2. Version del esquema */
IF OBJECT_ID(N'kb.VersionEsquema', N'U') IS NULL
BEGIN
    CREATE TABLE kb.VersionEsquema
    (
        Version     int          NOT NULL CONSTRAINT PK_VersionEsquema PRIMARY KEY,
        Descripcion nvarchar(200) NOT NULL,
        AplicadaEn  datetime2(0) NOT NULL CONSTRAINT DF_VersionEsquema_AplicadaEn DEFAULT (SYSUTCDATETIME())
    );
    INSERT INTO kb.VersionEsquema (Version, Descripcion) VALUES (1, N'Esquema inicial de la base de conocimiento');
END
GO

/* ------------------------------------------------------------------ 3. Importaciones
   Una fila por cada subida de notas: quien la hizo, cuando y que paso. El detalle por
   archivo (nuevos, omitidos, secretos enmascarados, errores) va en Informe como JSON. */
IF OBJECT_ID(N'kb.Importacion', N'U') IS NULL
BEGIN
    CREATE TABLE kb.Importacion
    (
        ImportacionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Importacion PRIMARY KEY CLUSTERED,
        UsuarioId     uniqueidentifier NOT NULL,           -- CAT_USUARIOS.USUARIO_ID en SEGURIDAD_PROSUR
        UsuarioNombre nvarchar(100)    NOT NULL,
        UsuarioEmail  nvarchar(100)    NOT NULL,
        Origen        nvarchar(20)     NOT NULL,
        Estado        nvarchar(20)     NOT NULL CONSTRAINT DF_Importacion_Estado DEFAULT (N'EnCurso'),
        IniciadaEn    datetime2(0)     NOT NULL CONSTRAINT DF_Importacion_IniciadaEn DEFAULT (SYSUTCDATETIME()),
        TerminadaEn   datetime2(0)     NULL,
        Archivos      int NOT NULL CONSTRAINT DF_Importacion_Archivos      DEFAULT (0),
        Nuevos        int NOT NULL CONSTRAINT DF_Importacion_Nuevos        DEFAULT (0),
        Actualizados  int NOT NULL CONSTRAINT DF_Importacion_Actualizados  DEFAULT (0),
        SinCambios    int NOT NULL CONSTRAINT DF_Importacion_SinCambios    DEFAULT (0),
        Omitidos      int NOT NULL CONSTRAINT DF_Importacion_Omitidos      DEFAULT (0),
        ConSecretos   int NOT NULL CONSTRAINT DF_Importacion_ConSecretos   DEFAULT (0),
        Errores       int NOT NULL CONSTRAINT DF_Importacion_Errores       DEFAULT (0),
        Informe       nvarchar(max)    NULL,
        CONSTRAINT CK_Importacion_Origen  CHECK (Origen IN (N'archivos', N'zip', N'carpeta')),
        CONSTRAINT CK_Importacion_Estado  CHECK (Estado IN (N'EnCurso', N'Completada', N'Fallida')),
        CONSTRAINT CK_Importacion_Informe CHECK (Informe IS NULL OR ISJSON(Informe) = 1)
    );
    CREATE INDEX IX_Importacion_Fecha ON kb.Importacion (IniciadaEn DESC);
    CREATE INDEX IX_Importacion_Usuario ON kb.Importacion (UsuarioId, IniciadaEn DESC);
END
GO

/* ------------------------------------------------------------------- 4. Documentos
   ContenidoMd    : Markdown ya con los secretos enmascarados (nunca el original).
   ContenidoTexto : el mismo contenido sin marcas de Markdown; es lo que indexa la busqueda.
   HashOrigen     : SHA-256 del archivo original; si no cambia, la importacion lo omite.
   Eliminado      : borrado logico (se conserva el historial y los enlaces entrantes). */
IF OBJECT_ID(N'kb.Documento', N'U') IS NULL
BEGIN
    CREATE TABLE kb.Documento
    (
        DocumentoId          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Documento PRIMARY KEY CLUSTERED,
        RutaRelativa         nvarchar(450)  NOT NULL,          -- ruta dentro del vault, con "/" (ej. SICPRO/Modulo Banco.md)
        Titulo               nvarchar(300)  NOT NULL,
        Carpeta              nvarchar(450)  NOT NULL CONSTRAINT DF_Documento_Carpeta DEFAULT (N''),
        Resumen              nvarchar(500)  NULL,
        Estado               nvarchar(50)   NULL,               -- "status" del frontmatter de Obsidian
        ContenidoMd          nvarchar(max)  NOT NULL,
        ContenidoTexto       nvarchar(max)  NOT NULL,
        HashOrigen           binary(32)     NOT NULL,
        SecretosEnmascarados int            NOT NULL CONSTRAINT DF_Documento_Secretos DEFAULT (0),
        ModificadoEnOrigen   datetime2(0)   NULL,
        Version              int            NOT NULL CONSTRAINT DF_Documento_Version DEFAULT (1),
        Eliminado            bit            NOT NULL CONSTRAINT DF_Documento_Eliminado DEFAULT (0),
        EliminadoEn          datetime2(0)   NULL,
        CreadoEn             datetime2(0)   NOT NULL CONSTRAINT DF_Documento_CreadoEn DEFAULT (SYSUTCDATETIME()),
        CreadoPorId          uniqueidentifier NOT NULL,
        CreadoPorNombre      nvarchar(100)  NOT NULL,
        ActualizadoEn        datetime2(0)   NOT NULL CONSTRAINT DF_Documento_ActualizadoEn DEFAULT (SYSUTCDATETIME()),
        ActualizadoPorId     uniqueidentifier NOT NULL,
        ActualizadoPorNombre nvarchar(100)  NOT NULL,
        CONSTRAINT UQ_Documento_Ruta UNIQUE (RutaRelativa),
        -- Defensa en profundidad contra rutas peligrosas (la aplicacion tambien las valida).
        CONSTRAINT CK_Documento_Ruta CHECK (
            RutaRelativa LIKE N'%.md'
            AND RutaRelativa NOT LIKE N'/%'
            AND RutaRelativa NOT LIKE N'%\%'
            AND RutaRelativa NOT LIKE N'%..%'),
        CONSTRAINT CK_Documento_Titulo CHECK (LEN(Titulo) > 0),
        CONSTRAINT CK_Documento_Eliminado CHECK ((Eliminado = 0 AND EliminadoEn IS NULL) OR (Eliminado = 1 AND EliminadoEn IS NOT NULL))
    );
    CREATE INDEX IX_Documento_Carpeta    ON kb.Documento (Eliminado, Carpeta, Titulo) INCLUDE (Resumen, ActualizadoEn);
    CREATE INDEX IX_Documento_Actualizado ON kb.Documento (Eliminado, ActualizadoEn DESC);
END
GO

/* Historial: cada cambio de contenido queda guardado con quien lo subio. */
IF OBJECT_ID(N'kb.DocumentoVersion', N'U') IS NULL
BEGIN
    CREATE TABLE kb.DocumentoVersion
    (
        VersionId     bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentoVersion PRIMARY KEY CLUSTERED,
        DocumentoId   int              NOT NULL,
        Version       int              NOT NULL,
        ContenidoMd   nvarchar(max)    NOT NULL,
        HashOrigen    binary(32)       NOT NULL,
        ImportacionId int              NULL,
        UsuarioId     uniqueidentifier NOT NULL,
        UsuarioNombre nvarchar(100)    NOT NULL,
        CreadoEn      datetime2(0)     NOT NULL CONSTRAINT DF_DocumentoVersion_CreadoEn DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_DocumentoVersion_Documento   FOREIGN KEY (DocumentoId)   REFERENCES kb.Documento (DocumentoId) ON DELETE CASCADE,
        CONSTRAINT FK_DocumentoVersion_Importacion FOREIGN KEY (ImportacionId) REFERENCES kb.Importacion (ImportacionId),
        CONSTRAINT UQ_DocumentoVersion UNIQUE (DocumentoId, Version)
    );
    CREATE INDEX IX_DocumentoVersion_Importacion ON kb.DocumentoVersion (ImportacionId) WHERE ImportacionId IS NOT NULL;
END
GO

/* ---------------------------------------------------- 5. Alias, etiquetas y enlaces */
-- "aliases" del frontmatter: sirven para resolver enlaces [[Nombre alterno]].
IF OBJECT_ID(N'kb.DocumentoAlias', N'U') IS NULL
BEGIN
    CREATE TABLE kb.DocumentoAlias
    (
        DocumentoId int           NOT NULL,
        Alias       nvarchar(300) NOT NULL,
        CONSTRAINT PK_DocumentoAlias PRIMARY KEY CLUSTERED (DocumentoId, Alias),
        CONSTRAINT FK_DocumentoAlias_Documento FOREIGN KEY (DocumentoId) REFERENCES kb.Documento (DocumentoId) ON DELETE CASCADE
    );
    CREATE INDEX IX_DocumentoAlias_Alias ON kb.DocumentoAlias (Alias);
END
GO

IF OBJECT_ID(N'kb.Etiqueta', N'U') IS NULL
BEGIN
    CREATE TABLE kb.Etiqueta
    (
        EtiquetaId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Etiqueta PRIMARY KEY CLUSTERED,
        Nombre     nvarchar(100) NOT NULL,                     -- sin "#"
        CONSTRAINT UQ_Etiqueta_Nombre UNIQUE (Nombre),
        CONSTRAINT CK_Etiqueta_Nombre CHECK (LEN(Nombre) > 0 AND Nombre NOT LIKE N'#%')
    );
END
GO

IF OBJECT_ID(N'kb.DocumentoEtiqueta', N'U') IS NULL
BEGIN
    CREATE TABLE kb.DocumentoEtiqueta
    (
        DocumentoId int NOT NULL,
        EtiquetaId  int NOT NULL,
        CONSTRAINT PK_DocumentoEtiqueta PRIMARY KEY CLUSTERED (DocumentoId, EtiquetaId),
        CONSTRAINT FK_DocumentoEtiqueta_Documento FOREIGN KEY (DocumentoId) REFERENCES kb.Documento (DocumentoId) ON DELETE CASCADE,
        CONSTRAINT FK_DocumentoEtiqueta_Etiqueta  FOREIGN KEY (EtiquetaId)  REFERENCES kb.Etiqueta (EtiquetaId)
    );
    CREATE INDEX IX_DocumentoEtiqueta_Etiqueta ON kb.DocumentoEtiqueta (EtiquetaId, DocumentoId);
END
GO

/* Enlaces [[Destino#Seccion|Texto]] entre notas. DocumentoDestinoId queda NULL si el destino no existe
   (enlace roto); se resuelve de nuevo en cada importacion. "Quien enlaza a esta nota" = filtrar por destino. */
IF OBJECT_ID(N'kb.Enlace', N'U') IS NULL
BEGIN
    CREATE TABLE kb.Enlace
    (
        EnlaceId           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Enlace PRIMARY KEY CLUSTERED,
        DocumentoOrigenId  int           NOT NULL,
        Destino            nvarchar(450) NOT NULL,             -- texto tal como aparece en la nota
        Seccion            nvarchar(300) NULL,
        TextoVisible       nvarchar(300) NULL,
        DocumentoDestinoId int           NULL,
        CONSTRAINT FK_Enlace_Origen  FOREIGN KEY (DocumentoOrigenId)  REFERENCES kb.Documento (DocumentoId) ON DELETE CASCADE,
        -- Sin cascada: dos rutas de cascada hacia Documento no estan permitidas; los borrados son logicos.
        CONSTRAINT FK_Enlace_Destino FOREIGN KEY (DocumentoDestinoId) REFERENCES kb.Documento (DocumentoId)
    );
    CREATE INDEX IX_Enlace_Origen  ON kb.Enlace (DocumentoOrigenId);
    CREATE INDEX IX_Enlace_Destino ON kb.Enlace (DocumentoDestinoId) WHERE DocumentoDestinoId IS NOT NULL;
END
GO

/* ------------------------------------------------------- 6. Reglas de omision
   Carpetas o archivos que el importador nunca debe leer. Editable sin cambiar codigo. */
IF OBJECT_ID(N'kb.ReglaOmision', N'U') IS NULL
BEGIN
    CREATE TABLE kb.ReglaOmision
    (
        ReglaId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReglaOmision PRIMARY KEY CLUSTERED,
        Tipo    nvarchar(10)  NOT NULL,
        Patron  nvarchar(450) NOT NULL,                        -- nombre de carpeta/archivo (coincidencia exacta, sin distinguir mayusculas)
        Motivo  nvarchar(200) NULL,
        Activa  bit           NOT NULL CONSTRAINT DF_ReglaOmision_Activa DEFAULT (1),
        CONSTRAINT UQ_ReglaOmision UNIQUE (Tipo, Patron),
        CONSTRAINT CK_ReglaOmision_Tipo CHECK (Tipo IN (N'carpeta', N'archivo'))
    );

    INSERT INTO kb.ReglaOmision (Tipo, Patron, Motivo) VALUES
        (N'carpeta', N'.obsidian',                        N'Configuracion interna de Obsidian'),
        (N'carpeta', N'Cambios de contrase' + NCHAR(241) + N'a chemicloud', N'Contiene credenciales; no debe publicarse');
END
GO

/* ---------------------------------------------------- 7. Busqueda de texto completo
   Opcional: solo se crea si el servidor tiene Full-Text Search instalado (SQL Server Express
   sin "Advanced Services" NO lo trae). Sin el, la aplicacion busca con LIKE, que para unos
   cientos de notas es instantaneo. Cuando se instale Full-Text basta con volver a ejecutar
   este mismo script: es re-ejecutable y solo agregara lo que falte. */
IF CAST(SERVERPROPERTY('IsFullTextInstalled') AS int) = 1
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'ftcSoporteKB')
        EXEC (N'CREATE FULLTEXT CATALOG ftcSoporteKB WITH ACCENT_SENSITIVITY = OFF AS DEFAULT');

    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'kb.Documento'))
        EXEC (N'CREATE FULLTEXT INDEX ON kb.Documento
                    (Titulo LANGUAGE 3082, ContenidoTexto LANGUAGE 3082)
                    KEY INDEX PK_Documento ON ftcSoporteKB
                    WITH (CHANGE_TRACKING = AUTO, STOPLIST = SYSTEM)');
END
ELSE
    PRINT N'AVISO: Full-Text Search no esta instalado en este servidor; se omite el indice de texto completo.';
GO

/* ------------------------------------------------------------ 8. Roles de la aplicacion
   kb_lector   : solo lectura (lo usa la API para consultas).
   kb_escritor : lectura y escritura (lo usa la API al importar notas).
   Los logins/usuarios se asignan a estos roles; ver la plantilla comentada abajo. */
IF DATABASE_PRINCIPAL_ID(N'kb_lector') IS NULL   CREATE ROLE kb_lector;
IF DATABASE_PRINCIPAL_ID(N'kb_escritor') IS NULL CREATE ROLE kb_escritor;
GO

GRANT SELECT ON SCHEMA::kb TO kb_lector;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::kb TO kb_escritor;
GO

/* ------------------------------------------------------- 9. Verificacion (solo lectura) */
SELECT  N'Tablas'  AS Que, COUNT(*) AS Total FROM sys.tables WHERE schema_id = SCHEMA_ID(N'kb')
UNION ALL
SELECT  N'Indice de texto completo (0 = no instalado u omitido)', COUNT(*) FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'kb.Documento') AND is_enabled = 1
UNION ALL
SELECT  N'Reglas de omision', COUNT(*) FROM kb.ReglaOmision;
GO

/* =============================================================================
   PLANTILLA OPCIONAL: usuarios de la API (descomentar y reemplazar contrasenas).
   Requiere permiso para crear logins. Genera contrasenas largas y aleatorias y
   guardalas en los secretos de la API, nunca en este archivo.

   USE master;
   CREATE LOGIN kb_api_lector  WITH PASSWORD = N'<<REEMPLAZAR>>', CHECK_POLICY = ON, DEFAULT_DATABASE = SoporteKB;
   CREATE LOGIN kb_api_escritor WITH PASSWORD = N'<<REEMPLAZAR>>', CHECK_POLICY = ON, DEFAULT_DATABASE = SoporteKB;
   GO
   USE SoporteKB;
   CREATE USER kb_api_lector   FOR LOGIN kb_api_lector;
   CREATE USER kb_api_escritor FOR LOGIN kb_api_escritor;
   ALTER ROLE kb_lector   ADD MEMBER kb_api_lector;
   ALTER ROLE kb_escritor ADD MEMBER kb_api_escritor;
   GO
   ============================================================================= */
