# Soporte Blunk – API (solo lectura sobre ServiceDesk Plus 10.5)

Backend en .NET 10 con arquitectura limpia (`Domain` → `Application` → `Infrastructure` → `Api`).
Lee la base `servicedesk10_5` (SQL Server) con un usuario de solo lectura. **Solo expone GET**, salvo `POST /v1/auth/login`; cualquier otro método responde 405.

## Autenticación
Todos los endpoints (menos `/health` y el login) exigen un JWT propio de esta API (`Authorization: Bearer …`).
- `POST /v1/auth/login` `{ email, password }`: la contraseña **se valida en el servicio de Seguridad** (`POST api/Seguridad/Login` de `apiseguridad`); esta API nunca lee ni guarda contraseñas ni hashes.
- Con la respuesta se exige `TIPO='U'`, `STATUS='A'` e `ID_ROL` = ROL-SISTEMAS (el login de Seguridad no lo filtra). Si no, 403.
- Tras 5 fallos por correo se bloquea 15 min (429 + `Retry-After`); además hay un tope global de 30 intentos/min.
- Sesión de 8 h. `GET /v1/auth/me` devuelve el usuario de la sesión.

Configuración (`appsettings.json` / variables de entorno):

| Clave | Para qué | Dónde |
|---|---|---|
| `Seguridad:BaseUrl` | URL base de apiseguridad (HTTPS; HTTP solo en localhost) | appsettings |
| `Auth:RolSistemasId` | ID del rol ROL-SISTEMAS | appsettings |
| `Auth:JwtKey` | Clave de firma, **mínimo 32 caracteres** | secretos / `Auth__JwtKey` (obligatoria, la API no arranca sin ella) |
| `ConnectionStrings:Msdb` | `msdb` del servidor donde corren los jobs (hoy 10.0.3.8, mismo usuario de solo lectura que ServiceDesk, cambiando `Database=msdb`). Opcional: sin ella `GET /v1/utilerias/jobs` responde 503 | secretos / `ConnectionStrings__Msdb` |
| `ConnectionStrings:Microsip` / `ConnectionStrings:MicrosipEscritura` | Servidor con los linked servers `MICROSIP_CONFIG` y `FORTIA` (hoy 10.0.3.8). `Microsip` es de lectura (puede ser el mismo usuario de ServiceDesk con `Database=master`); `MicrosipEscritura` es opcional y, si no se define, se usa la de lectura. Sin ellas, los endpoints de usuarios MICROSIP responden 503 | secretos / variables de entorno |
| `ConnectionStrings:SoporteKB` | Base de conocimiento (`SoporteKB`, con escritura). Opcional: sin ella `GET /v1/conocimiento/estado` responde 503 | secretos / `ConnectionStrings__SoporteKB` |

La base `SoporteKB` se crea con `db/001_crear_SoporteKB.sql` (re-ejecutable; el índice de texto completo solo se crea si el servidor tiene Full-Text Search). Para producción, crea los usuarios `kb_api_*` de la plantilla del script y **no uses una cuenta sysadmin**.

## Ejecutar
```bash
cd src/SoporteBlunk.Api
dotnet user-secrets set "ConnectionStrings:ServiceDesk" "Server=10.0.3.8;Database=servicedesk10_5;User Id=S4CONSULTA;Password=<pwd>;TrustServerCertificate=True;ApplicationIntent=ReadOnly"
dotnet user-secrets set "Auth:JwtKey" "<cadena aleatoria de 48+ caracteres>"
dotnet run          # Swagger en http://localhost:5138/swagger (solo Development)
```
En otros entornos se usa la variable `ConnectionStrings__ServiceDesk` (ver `.env.example`). La contraseña nunca va en el repositorio.

## Endpoints (`/v1`)
| Área | Rutas |
|---|---|
| Dashboard | `dashboard/resumen`, `dashboard/tendencia?agrupacion=Dia\|Semana\|Mes`, `dashboard/por-{categoria,prioridad,modo,grupo,tecnico,urgencia,tipo-solicitud}`, `dashboard/sla` |
| Tickets | `tickets` (filtros, paginado), `tickets/{id}`, `tickets/{id}/historial`, `tickets/{id}/tiempos`, `tickets/export` (Excel, máx. 10,000 filas) |
| Técnicos | `tecnicos`, `tecnicos/carga`, `tecnicos/{id}/carga`, `tecnicos/{id}/productividad?desde=&hasta=`, `tecnicos/productividad?desde=&hasta=` (equipo completo), `tecnicos/{id}/productividad/tendencia?desde=&hasta=&agrupacion=` (evolución por periodo) |
| Auditoría | `auditoria?maximo=` (1-300, por defecto 100): línea de tiempo unificada de Conocimiento (importaciones), MICROSIP (`aud.CambioMicrosip`) y Bitácora (`bitacora.ChecklistDiario`), la más reciente primero. Si una fuente falla (p. ej. un ambiente sin algún script de SoporteKB corrido) se omite y se muestran las demás; si fallan las tres, responde 503. |
| Catálogos | `catalogos/{estados,prioridades,grupos,categorias,subcategorias,items,urgencias,impactos,modos,tipos-solicitud,niveles,departamentos}` |

Filtros de tickets: `estadoId, tecnicoId, grupoId, categoriaId, prioridadId, desde, hasta, vencido, sinResponder, buscar, orden, page, pageSize (máx. 200)`.
`sinResponder=true`: abiertos sin primera respuesta (no una columna cruda: `ISPENDING=1 AND RESPONDEDTIME=0`). Nota: en esta
instancia de ServiceDesk casi ningún ticket tiene `DUEBYTIME`/`FR_DUETIME` configurado (fecha límite de SLA), por eso no hay
un filtro de "vence pronto"; `sinResponder` + `orden=CreadoAsc` es el sustituto que sí tiene datos reales.
Las fechas `desde/hasta` son días en hora de México (hasta inclusivo) y se aplican a la fecha de creación. Las fechas de salida llevan offset de México.

Productividad de técnicos: la primera respuesta se mide desde que el ticket quedó **asignado al técnico actual**
(`WorkOrderStates.ASSIGNEDTIME`), no desde la creación del ticket; si se reasignó después de que otra persona ya
había respondido, ese ticket no cuenta en el promedio (no fue este técnico quien respondió). `cumplimientoSlaPct` es
`(asignados - vencidos) / asignados * 100` (100 si no hay asignados). `tecnicos/productividad` trae la misma forma
que `tecnicos/{id}/productividad` para cada técnico con tickets en el periodo, pensado para comparar al equipo.
`/health` responde el estado del servicio.

## Notas sobre los datos de ServiceDesk
- Los tiempos se guardan en ms UTC desde 1970; `0` significa "sin dato".
- "Abierto" = estado con `ISPENDING = 1` (Open, Onhold, Assigned, In Progress); "cerrado" = Closed/Resolved.
- No hay tabla de técnicos: se consideran técnicos los usuarios que son `OWNERID` de algún ticket.
- `tickets/{id}/tiempos`: `tiempoRegistrado` es `TIMESPENT` tal cual; su unidad no está confirmada.
- Las prioridades tienen nombres numéricos (1–20, 99) en esta instalación.

## Base de conocimiento (subida de notas de Obsidian)
Los `.md` **se suben** al sistema; la API nunca lee carpetas del disco.

| Método | Ruta | Para qué |
|---|---|---|
| `POST` | `/v1/conocimiento/importaciones` | Sube notas (`multipart/form-data`): campo `archivos` (varios `.md`, una carpeta o un `.zip`), opcional `rutas` (ruta relativa de cada archivo, mismo orden) y `modificados` (ms Unix). Devuelve el informe por archivo. |
| `GET` | `/v1/conocimiento/importaciones` | Bitácora: quién subió qué y cuándo. |
| `GET` | `/v1/conocimiento/importaciones/{id}` | Detalle e informe de una subida. |
| `GET` | `/v1/conocimiento/estado` | Versión del esquema, notas guardadas y si hay texto completo. |

Qué hace cada subida:
- **Rutas:** se normalizan y se rechazan `..`, rutas absolutas y caracteres de control. Solo `.md`; lo demás se omite. Las carpetas de `kb.ReglaOmision` (p. ej. `.obsidian`) se omiten.
- **Secretos:** antes de guardar nada se ocultan contraseñas, tokens, JWT, llaves privadas y llaves AWS/GitHub (`[OCULTO]`). El informe dice **en qué líneas** (nunca el valor) para revisarlas. No detecta secretos escritos en prosa.
- **Privacidad:** los comentarios de Obsidian (`%% … %%`) no se guardan.
- **Idempotente:** si el hash del archivo no cambió queda como `SinCambios`; si cambió se actualiza y se guarda una versión nueva en el historial.
- **Todo o nada:** las notas de una subida se guardan en una transacción; si algo falla no queda ninguna y la subida se registra como `Fallida`.
- **Enlaces `[[wiki]]`:** se resuelven con las reglas de Obsidian; los rotos se resuelven solos cuando después se sube la nota destino.
- **Límites** (`Conocimiento:*`): 1 MB por archivo, 500 archivos por subida, 60 MB por petición, 100 MB descomprimidos y 2000 entradas por zip (anti bomba de compresión); 10 subidas por minuto.
- **Auditoría:** cada subida guarda el usuario (id, nombre, correo) tomado del token de sesión.

Nota para `sqlcmd`: usa `-I` si ejecutas consultas manuales de escritura sobre `SoporteKB` (las tablas tienen índices filtrados que exigen `QUOTED_IDENTIFIER ON`; el cliente de .NET ya lo activa).

## Utilerías

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/v1/utilerias/jobs?soloVigilados=true` | Jobs de SQL Server Agent: resultado de la última ejecución de cada job y de cada paso, si está corriendo, próxima ejecución. Por defecto solo los de `Utilerias:JobsVigilados` (appsettings); `soloVigilados=false` lista todos. Solo lectura (SELECT sobre msdb). |

### Usuarios MICROSIP y FORTIA

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/v1/utilerias/microsip/usuarios` | Usuarios de MICROSIP (tipo U) con su clave de FORTIA, nombre, empresa y puesto (`USUARIOS_DATOS_ADICIONALES`). |
| `GET` | `/v1/utilerias/microsip/trabajadores/{clave}` | Trabajador de FORTIA (`FORTIA.PROSUR_PRIME.dbo.RH_TRAB`). 404 si no existe. |
| `PUT` | `/v1/utilerias/microsip/usuarios/{id}/datos-adicionales` | Body `{ "claveFortia": 123 }`. **Única escritura sobre MICROSIP.** |
| `GET` | `/v1/utilerias/microsip/cambios` | Bitácora de cambios (quién, antes y después). |

El PUT: (1) lee el usuario y el trabajador; (2) **registra el cambio como `Pendiente` en `aud.CambioMicrosip` (SoporteKB; script `db/002_auditoria_microsip.sql`) antes de escribir; si la bitácora no responde no se escribe nada**; (3) hace `INSERT`/`UPDATE` por `EXEC ... AT [MICROSIP_CONFIG]` con parámetros (no concatena texto); (4) vuelve a leer y marca `Aplicado` o `NoVerificado` (o `Fallido`). Nombre, empresa y puesto salen de FORTIA en el servidor, no del cliente. Los nombres de linked server (`Utilerias:Microsip`) se validan al arrancar (solo letras, números y `_`). Límite: 20 cambios por minuto.

Estados: `Exitoso`, `Fallido`, `Reintento`, `Cancelado`, `EnEjecucion`, `SinHistorial`. Las fechas son la hora local del servidor SQL, sin zona. El usuario necesita poder leer `msdb.dbo.sysjobs*` (`S4CONSULTA` ya puede).

## Bitacora de actividades (reemplazo del Excel)

| Metodo | Ruta | Descripcion |
|---|---|---|
| `GET` | `/v1/bitacora/actividades?grupo=actividades-soft&incluirInactivas=` | Catalogo de actividades a revisar. |
| `POST` | `/v1/bitacora/actividades` | Crea una actividad `{ grupo, nombre }`. |
| `PUT` | `/v1/bitacora/actividades/{id}` | Renombra, activa/desactiva o reordena `{ nombre?, activa?, orden? }`. |
| `GET` | `/v1/bitacora/checklist?grupo=&fecha=yyyy-MM-dd` | Checklist de un dia: cada actividad activa (o con registro ese dia) con su estado. |
| `PUT` | `/v1/bitacora/checklist/{actividadId}?fecha=yyyy-MM-dd` | Guarda el estado de una actividad ese dia `{ estado, observacion? }` (upsert). |
| `GET` | `/v1/bitacora/resumen?grupo=&desde=&hasta=` | Conteo por estado de cada dia del rango (maximo 1 ano). |

"Grupo" identifica cada bitacora; hoy existen `actividades-soft`, `respaldos`, `camaras` y `depuracion-respaldos`
(ver `Application.Common.BitacoraGrupos`). Las tres primeras son checklist diario; `depuracion-respaldos` es mensual
(el frontend usa siempre el dia 1 del mes como Fecha, la API no distingue granularidad). El
esquema (`db/003_bitacora.sql`, tabla `bitacora.*` en SoporteKB) es generico: una bitacora nueva con el mismo
patron de checklist diario (por ejemplo la hoja "Respaldos" del Excel) solo necesita agregar su slug ahi, sin
tocar la base. Estados: `Ok`, `Advertencia`, `Falla`, `Critico`, `NoAplica`; los tres primeros exigen
`observacion` (la base tambien lo exige con un CHECK). Limite de escritura: 20/min (bucket compartido "escritura").

## Pruebas
```bash
dotnet test      # las de integración se omiten si no existe ConnectionStrings__ServiceDesk
```
