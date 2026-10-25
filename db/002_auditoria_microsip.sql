/* ============================================================================
   002_auditoria_microsip.sql
   Bitacora de cambios que la consola hace en MICROSIP (USUARIOS_DATOS_ADICIONALES).
   Se ejecuta en la base SoporteKB (la misma de la base de conocimiento). Es idempotente.

   La API registra cada cambio ANTES de aplicarlo (Resultado = 'Pendiente') y despues lo marca como
   'Aplicado' o 'Fallido'. Si esta tabla no existe o no responde, la API NO escribe en MICROSIP.

   Solo ASCII (sin acentos) para que no dependa de la codificacion del archivo.
   Ejecutar con:  sqlcmd -S <servidor> -d SoporteKB -U <usuario> -P <clave> -C -I -i 002_auditoria_microsip.sql
   ============================================================================ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF SCHEMA_ID(N'aud') IS NULL EXEC(N'CREATE SCHEMA aud AUTHORIZATION dbo;');
GO

IF OBJECT_ID(N'aud.CambioMicrosip', N'U') IS NULL
BEGIN
    CREATE TABLE aud.CambioMicrosip
    (
        CambioId            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_aud_CambioMicrosip PRIMARY KEY,
        Fecha               datetime2(0)  NOT NULL CONSTRAINT DF_aud_CambioMicrosip_Fecha DEFAULT (SYSDATETIME()),
        UsuarioEmail        nvarchar(256) NOT NULL,   -- quien hizo el cambio en la consola
        UsuarioNombre       nvarchar(200) NOT NULL,
        MicrosipUsuarioId   int           NOT NULL,   -- USUARIOS.USUARIO_ID de MICROSIP
        MicrosipNombre      nvarchar(200) NOT NULL,   -- USUARIOS.NOMBRE al momento del cambio
        Accion              varchar(12)   NOT NULL,   -- 'Insertar' | 'Actualizar'
        Resultado           varchar(12)   NOT NULL,   -- 'Pendiente' | 'Aplicado' | 'Fallido' | 'NoVerificado'
        Antes               nvarchar(1000) NULL,      -- JSON de los datos adicionales previos
        Despues             nvarchar(1000) NOT NULL,  -- JSON de los datos que se escribieron
        Error               nvarchar(1000) NULL,
        CONSTRAINT CK_aud_CambioMicrosip_Accion    CHECK (Accion IN ('Insertar', 'Actualizar')),
        CONSTRAINT CK_aud_CambioMicrosip_Resultado CHECK (Resultado IN ('Pendiente', 'Aplicado', 'Fallido', 'NoVerificado'))
    );
    CREATE INDEX IX_aud_CambioMicrosip_Fecha ON aud.CambioMicrosip (Fecha DESC);
    CREATE INDEX IX_aud_CambioMicrosip_Usuario ON aud.CambioMicrosip (MicrosipUsuarioId, Fecha DESC);
    PRINT 'Tabla aud.CambioMicrosip creada.';
END
ELSE PRINT 'Tabla aud.CambioMicrosip ya existe.';
GO

/* Permisos: la API de lectura solo consulta la bitacora; la que escribe tambien la registra. */
IF DATABASE_PRINCIPAL_ID(N'kb_lector')   IS NOT NULL GRANT SELECT ON SCHEMA::aud TO kb_lector;
IF DATABASE_PRINCIPAL_ID(N'kb_escritor') IS NOT NULL GRANT SELECT, INSERT, UPDATE ON SCHEMA::aud TO kb_escritor;
GO

PRINT 'Listo.';
GO
