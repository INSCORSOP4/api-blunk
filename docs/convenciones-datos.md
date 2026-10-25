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

## Estado de la migración

| Fase | Alcance | Estado |
|---|---|---|
| 0 | Contextos y convenciones | Hecha |
| 1 | ServiceDesk: catálogos, tickets, técnicos, dashboard | Hecha y validada contra la base real |
| 2 | SoporteKB: Bitácora, Auditoría de Microsip, Archivos | Hecha y validada contra la base real |
| 3 | SoporteKB: Conocimiento | Pendiente |
| 4 | msdb: jobs | Pendiente |
| 5 | Microsip y FORTIA (linked server) | Se quedan en Dapper por decisión |

## Cómo se valida cada fase

Cada repositorio migrado conserva su versión Dapper original en `tests/.../Legacy/`, solo como referencia:

- **Sin base de datos**: tests que revisan el SQL que genera EF (tablas, joins, filtros, orden) y las conversiones.
- **Contra la base real** (se omiten sin `ConnectionStrings__ServiceDesk`): paridad EF contra Dapper en varios rangos y filtros, y una
  sonda que compara todo el modelo con `INFORMATION_SCHEMA` (tipos y existencia de columnas).

Para ejecutarlos con la VPN activa:

```powershell
Get-Content .env | Where-Object { $_ -match '^\s*[A-Za-z_]' } | ForEach-Object { $k,$v = $_ -split '=',2; Set-Item "env:$k" $v }
dotnet test
```

Diferencias conocidas respecto a Dapper: los promedios de horas se calculan en `float` (antes `decimal`), por lo que pueden diferir
en los últimos decimales; los tiempos y los nombres completos se arman en código (LINQ no traduce `AT TIME ZONE`).

## Fase 2 (SoporteKB, con escritura): decisiones y diferencias respecto a Dapper

- **Contrato de errores intacto.** Sin cadena de SoporteKB el contexto se crea igual y falla al abrir la conexión con
  `KbNoDisponibleException` ("no está configurada"); con el servidor caído, `KbConexionInterceptor` convierte el fallo de conexión en
  `KbNoDisponibleException` ("No se pudo conectar...") como hacía la fábrica de conexiones, para que Auditoría no lo confunda con una
  tabla que falta. `KbDbContext.SaveChangesAsync` desenvuelve el `DbUpdateException` que EF pone alrededor.
- **La hora de las actualizaciones la pone el servidor SQL** (`GETDATE()` en `ExecuteUpdate`; `SYSDATETIME()` como default de las columnas
  de creación), igual que antes: nunca el reloj de la API.
- **`MERGE` → `UPDATE` y, si no había fila, `INSERT`.** Si dos usuarios crean el mismo registro del día a la vez, el segundo detecta la
  violación de llave y lo corrige en lugar de fallar (hay un test con 8 guardados simultáneos).
- **Auditoría de Microsip:** la fecha se interpreta como hora de México *en esa fecha*; antes se le sumaba el desplazamiento *actual* del
  servidor. Son iguales para fechas sin horario de verano (2022 en adelante).
- Los scripts de `db/` siguen siendo la única fuente del esquema de SoporteKB: EF no lo migra ni lo crea.

Los tests de escritura usan grupos y correos únicos por ejecución (`zz-t2-...`), corren en una colección propia (no en paralelo con los
de lectura) y borran lo que crean al terminar.
