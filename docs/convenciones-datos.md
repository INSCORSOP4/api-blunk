# Convenciones de acceso a datos (reestructura a EF Core)

Migración por fases, una rama por fase. Mientras dure, conviven Dapper (lo aún no migrado) y EF Core.

## Contextos (un `DbContext` por base)

| Contexto | Base | Modo | Cadena |
|---|---|---|---|
| `ServiceDeskDbContext` | ServiceDesk Plus (esquema de terceros) | Solo lectura, sin tracking, **sin migraciones** | `ConnectionStrings:ServiceDesk` (obligatoria) |
| `MsdbDbContext` | `msdb` del servidor de jobs | Solo lectura, sin tracking, **sin migraciones** | `ConnectionStrings:Msdb` (opcional → 503) |
| `KbDbContext` | SoporteKB (base propia) | Lectura y escritura | `ConnectionStrings:SoporteKB` (opcional → 503) |

Los contextos de solo lectura heredan de `ReadOnlyDbContext`: `SaveChanges` lanza de inmediato.
Si una cadena opcional falta, resolver su contexto lanza la misma excepción que antes (`KbNoDisponibleException`,
`UtileriaNoDisponibleException`) y el middleware la traduce a 503; la API sigue arrancando.

## Dónde va cada cosa

- **Entidades**: `Domain/Entities/<Base>/`. Una clase por tabla.
- **Configuración Fluent**: `Infrastructure/Persistence/Configurations/<Base>/` (un `IEntityTypeConfiguration<T>` por entidad).
  Cada contexto carga solo las del namespace de su base (`.ServiceDesk`, `.Msdb`, `.Kb`).
- **DTOs y filas de proyección**: `Application/Features/<Modulo>/Dtos/`, records públicos. Nunca privados dentro de un handler o repositorio.
- **Controller**: recibe parámetros, arma la query y la envía con `ISender`. Sin validaciones ni lógica; las reglas van en `AbstractValidator<TQuery>`.

## Reglas

1. LINQ sobre entidades; nada de SQL embebido en lo migrado.
2. Contra bases ajenas **nunca** `Migrate`/`EnsureCreated` ni cambios de esquema.
3. SoporteKB: los cambios de esquema siguen siendo scripts versionados en `db/` (no migraciones de EF).
4. Se omite lo que no se traduce bien: **Microsip** (`OPENQUERY`/`EXEC ... AT` sobre linked server a Firebird) y
   **FORTIA** (nombre de cuatro partes) se quedan en Dapper, en su propia rama.
5. Cada fase debe dejar los tests en verde y comparar su salida con la implementación Dapper anterior antes de retirarla.
