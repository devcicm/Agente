# IPC/DB - Interfaz de Integración con la Base de Datos

## 1. Objetivo
- Servir como intermediario independiente para operaciones SQL, reutilizable por el lector biométrico u otros procesos.
- Permitir que componentes (backend biométrico, scripts de prueba, pipelines) interactúen con la base sin compartir cadenas de conexión ni lógica SQL.

## 2. Arquitectura
- Proyecto Minimal API (.NET 8) ubicado en `IPC/DB`.
- Configuración centralizada en `appsettings.json` (sección `Database` con `Provider` y `ConnectionString`).
- Servicios `IDatabaseExecutor` + `SqlDatabaseExecutor` encapsulan el acceso `System.Data.SqlClient`.
- Endpoints expuestos:
  - `GET /ipc/db/health`: verifica que el IPC esté corriendo.
  - `POST /ipc/db/command`: ejecuta comandos DML (insert/update/delete) con parámetros opcionales.
  - `POST /ipc/db/query`: ejecuta consultas `SELECT` y devuelve cada fila como diccionario `{ columna: valor }`.

## 3. Casos de uso
- **Pruebas manuales**: disparar SQL sin abrir herramientas como SSMS.
- **Soporte al lector biométrico**: el backend principal puede delegar la persistencia al IPC vía HTTP.
- **CI/CD y mantenimiento**: scripts pueden usar la API para validar o poblar datos en ambientes controlados.

## 4. Extensibilidad
- `DbConnectionOptions.Provider` permite agregar nuevos ejecutores (ej. MySQL) sin cambiar los controladores.
- DTOs aceptan parámetros `Dictionary<string, object?>`, permitiendo consultas parametrizadas.
- Puede desplegarse como servicio Windows, contenedor o ejecutar en paralelo (puerto 5000 por defecto).

## 5. Operación
- Arranque: `dotnet run --project IPC/DB/DB.csproj`.
- Puertos: HTTP 5000 / HTTPS 5001 (configurable en `launchSettings.json` o variables de entorno).
- Logs: cada comando/consulta registra SQL ejecutado, filas afectadas y errores.

## 6. Consideraciones
- Actualmente no aplica autenticación/autorización; debe ejecutarse en redes de confianza o detrás de un proxy seguro.
- Se asume que el consumidor envía SQL seguro; idealmente, se should restringir a comandos predefinidos si se expone públicamente.
- Puede convivir con otros proyectos de la solución gracias a su carpeta y configuración independientes.
