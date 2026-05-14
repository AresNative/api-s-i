using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Caching.Memory;
using MyApiProject.Models;
using Microsoft.AspNetCore.SignalR;
using MyApiProject.Hubs;

namespace MyApiProject.Controllers.general
{
    [ApiExplorerSettings(GroupName = "general")]
    [Route("api/v1")]
    [ApiController]
    public partial class GeneralController : BaseController
    {
        private readonly ILogger<GeneralController> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;
        private readonly IHubContext<GeneralHubs> _hubContext;

        public GeneralController(
            IConfiguration configuration,
            IMemoryCache memoryCache,
            ILogger<GeneralController> logger,
            AuthUtils authUtils,
            IHubContext<GeneralHubs> hubContext)
            : base(configuration, memoryCache)
        {
            _logger = logger;
            _memoryCache = memoryCache;
            _authUtils = authUtils;
            _hubContext = hubContext;
        }

        // ── Helpers internos ──────────────────────────────────────────────────

        private async Task NotificarAsync(string evento, object payload) =>
            await _hubContext.Clients.Group("PedidosGeneral").SendAsync(evento, payload);

        private static async Task<List<Dictionary<string, object?>>> LeerFilasAsync(SqlDataReader reader)
        {
            var results = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                results.Add(row);
            }
            return results;
        }

        /// <summary>
        /// Valida tabla (contra INFORMATION_SCHEMA) y columna (regex de identificador).
        /// Retorna BadRequest listo para devolver si alguno falla, o null si todo es válido.
        /// </summary>
        private async Task<IActionResult?> ValidarTablaYColumnaAsync(string table, string? column = null)
        {
            var (tableValid, tableError) = await ValidateFromClauseAsync(table);
            if (!tableValid)
                return BadRequest(new { Message = $"Tabla inválida: {tableError}" });

            if (column != null)
            {
                var (colValid, colError) = ValidateIdentifier(column, "Columna");
                if (!colValid)
                    return BadRequest(new { Message = colError });
            }

            return null;
        }
        
        // ✅ Consulta por ID
        [Authorize]
        [HttpGet("consultar/{id}")]
        public async Task<IActionResult> ConsultarPorId(int id, [FromQuery] string? table = "general")
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string cacheKey = $"general_{table}_{id}";
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
                return Ok(cachedResults);

            string query = $"SELECT * FROM {table} WHERE id = @ID";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@ID", id);

            await using var reader = await command.ExecuteReaderAsync();
            var results = new List<Dictionary<string, object>>();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.GetValue(i);
                results.Add(row);
            }

            _memoryCache.Set(cacheKey, results, TimeSpan.FromMinutes(5));
            // Notificar a todos los clientes conectados
            await _hubContext.Clients.Group("PedidosGeneral")
                .SendAsync("DatosActualizados", new
                {
                    Tabla = table,
                    Accion = "ConsultaID",
                    TotalRegistros = results.Count,
                    Timestamp = DateTime.UtcNow
                });

            await _authUtils.InsertUserHistory(userId, "general load by id", $"Consulta en {table} con ID {id}");
            return Ok(results);
        }

        [HttpPost("consultar")]
        /* [ValidateToken] */
        public async Task<IActionResult> ConsultarGeneral(
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FiltrosRequest? request,
            [FromQuery] string fromClause = "")
        {
            request ??= new FiltrosRequest();

            if (string.IsNullOrWhiteSpace(fromClause))
                return BadRequest(new { Message = "El parámetro 'fromClause' es requerido." });

            // Validar el FROM clause antes de usarlo (regex + INFORMATION_SCHEMA)
            var (valid, error) = await ValidateFromClauseAsync(fromClause);
            if (!valid)
                return BadRequest(new { Message = $"FROM clause inválido: {error}" });

            return await ExecuteMassiveQueryAsync(request, fromClause, _logger);
        }
        // ✅ Registro dinámico con SignalR
        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> Registrar([FromBody] JObject data, [FromQuery] string? table = "general")
        {
            if (data == null) return BadRequest(new { Message = "JSON inválido" });
            
            var validacion = await ValidarTablaYColumnaAsync(table);
            if (validacion != null) return validacion;

            var properties = data.Properties()
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToList();

            if (!properties.Any())
                return BadRequest(new { Message = "El JSON no contiene propiedades válidas." });

            // Validar también los nombres de columna del JSON recibido
            foreach (var prop in properties)
            {
                var (colValid, colError) = ValidateIdentifier(prop.Name, "Columna");
                if (!colValid) return BadRequest(new { Message = colError });
            }

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            try
            {
                var columnNames = string.Join(", ", data.Properties().Select(p => $"[{p.Name}]"));
                var parameterNames = string.Join(", ", data.Properties().Select(p => $"@{p.Name}"));

                var query = $@"
                    INSERT INTO {table} ({columnNames})
                    OUTPUT INSERTED.*
                    VALUES ({parameterNames});";

                Console.WriteLine($"Query: {query}");

                await using var connection = await OpenConnectionAsync();
                Console.WriteLine("Conexión abierta exitosamente");

                await using var command = new SqlCommand(query, connection);

                foreach (var prop in data.Properties())
                {
                    var value = prop.Value?.ToObject<object>() ?? DBNull.Value;
                    command.Parameters.AddWithValue("@" + prop.Name, value);
                }
                
                await using var reader = await command.ExecuteReaderAsync();
                var insertedData = new Dictionary<string, object>();

                if (await reader.ReadAsync())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        insertedData[reader.GetName(i)] = reader.GetValue(i);
                    }
                }

                if (insertedData.Count > 0)
                {
                    await _hubContext.Clients.Group("PedidosGeneral")
                        .SendAsync("NuevoRegistro", new
                        {
                            Tabla = table,
                            Registro = insertedData,
                            Accion = "Insert",
                            UsuarioId = userId,
                            Timestamp = DateTime.UtcNow
                        });

                    await _authUtils.InsertUserHistory(userId, "general insert", $"Registro en {table} con ID {insertedData["id"]}");
                    _memoryCache.Remove($"general_all_{table}");
                    return Ok(new { Message = "Registro exitoso", Data = insertedData });
                }

                return StatusCode(500, new { Message = "Error al insertar el registro - no se obtuvieron datos de retorno" });
            }
            catch (SqlException sqlEx)
            {

                await _authUtils.InsertUserHistory(userId, "general insert sql error",
                    $"Error SQL al insertar en {table}: {sqlEx.Message} (Error #{sqlEx.Number})");

                return StatusCode(500, new
                {
                    Message = "Error de base de datos",
                    Details = sqlEx.Message,
                    ErrorNumber = sqlEx.Number,
                    LineNumber = sqlEx.LineNumber
                });
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                await _authUtils.InsertUserHistory(userId, "general insert error",
                    $"Error al insertar en {table}: {ex.Message}");

                return StatusCode(500, new
                {
                    Message = "Error interno del servidor",
                    Details = ex.Message,
                    StackTrace = ex.StackTrace,
                    InnerException = ex.InnerException?.Message
                });
            }
        }

        // ✅ Actualización dinámica - CORREGIDO: Devuelve todos los datos actualizados
        [Authorize]
        [HttpPut("update/{tabla}")]
        public async Task<IActionResult> Actualizar(string tabla, [FromBody] ActualizarRequest request)
        {
            var validacion = await ValidarTablaYColumnaAsync(tabla);
            if (validacion != null) return validacion;

            var filtros = request.Filtros?
                .Where(f => !string.IsNullOrWhiteSpace(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
                .ToList();

            if (filtros == null || !filtros.Any())
                return BadRequest(new { Message = "Se requieren filtros válidos para la actualización." });

            var whereConditions = new List<(string column, string op, string value)>();
            foreach (var f in filtros)
            {
                var op = NormalizeSimpleOperator(f.Operator);
                whereConditions.Add((f.Key, op, f.Value));
            }

            var setProperties = request.Data.Properties()
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToList();

            if (!setProperties.Any())
                return BadRequest(new { Message = "El JSON no contiene propiedades válidas para actualizar." });

            // Validar también los nombres de columna del JSON recibido
            foreach (var prop in setProperties)
            {
                var (colValid, colError) = ValidateIdentifier(prop.Name, "Columna");
                if (!colValid) return BadRequest(new { Message = colError });
            }

            var setClause = string.Join(", ", setProperties.Select(p => $"[{p.Name}] = @s_{p.Name}"));

            // Construir cláusula WHERE
            var whereParts = new List<string>();
            for (int i = 0; i < whereConditions.Count; i++)
                whereParts.Add($"[{whereConditions[i].column}] {whereConditions[i].op} @w{i}");
            var whereClause = string.Join(" AND ", whereParts);
            var updateQuery = $"UPDATE [{tabla}] SET {setClause} WHERE {whereClause}";
            var selectQuery = $"SELECT * FROM [{tabla}] WHERE {whereClause}";

            try
            {
                await using var connection = await OpenConnectionAsync();

                // ---- UPDATE (sin transacción) ----
                int rowsAffected;
                using (var updateCmd = new SqlCommand(updateQuery, connection))
                {
                    // Parámetros SET
                    foreach (var prop in setProperties)
                        updateCmd.Parameters.AddWithValue($"@s_{prop.Name}", prop.Value?.ToObject<object>() ?? DBNull.Value);

                    // Parámetros WHERE
                    for (int i = 0; i < whereConditions.Count; i++)
                    {
                        var (_, op, value) = whereConditions[i];
                        var pName = $"@w{i}";
                        var paramValue = (op == "LIKE" && !value.StartsWith("%") && !value.EndsWith("%")) ? $"%{value}%" : value;
                        var param = QB.CreateTypedParameter(pName, paramValue);
                        updateCmd.Parameters.Add(param);
                    }

                    rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                } // updateCmd se cierra aquí

                if (rowsAffected == 0)
                    return NotFound(new { Message = "Registro no encontrado o no se pudo actualizar." });

                // ---- SELECT (usando la misma conexión, pero con nuevo comando) ----
                List<Dictionary<string, object?>> updatedResults;
                using (var selectCmd = new SqlCommand(selectQuery, connection))
                {
                    // Recrear parámetros WHERE
                    for (int i = 0; i < whereConditions.Count; i++)
                    {
                        var (_, op, value) = whereConditions[i];
                        var pName = $"@w{i}";
                        var paramValue = (op == "LIKE" && !value.StartsWith("%") && !value.EndsWith("%")) ? $"%{value}%" : value;
                        var param = QB.CreateTypedParameter(pName, paramValue);
                        selectCmd.Parameters.Add(param);
                    }

                    await using var reader = await selectCmd.ExecuteReaderAsync();
                    updatedResults = await LeerFilasAsync(reader);
                }

                // Limpiar caché
                foreach (var f in filtros)
                    _cache.Remove($"general_{tabla}_{f.Key}_{f.Value}");
                _cache.Remove($"general_all_{tabla}");

                // Notificar
                var notifId = string.Join("_", filtros.Select(f => $"{f.Key}_{f.Value}"));
                await NotificarAsync("RegistroActualizado", new
                {
                    Tabla = tabla,
                    Filtros = notifId,
                    Accion = "Update",
                    Timestamp = DateTime.UtcNow,
                    RegistrosAfectados = updatedResults.Count
                });

                return Ok(new { Message = "Actualización exitosa.", RegistrosAfectados = updatedResults.Count, Data = updatedResults });
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Actualizar tabla={tabla}");
            }
        }

        // ✅ Eliminación lógica - CORREGIDO: Devuelve los datos antes de archivar
        [Authorize]
        [HttpDelete("archivar/{id}")]
        public async Task<IActionResult> Archivar(int id, [FromQuery] string? column = "id", [FromQuery] string? table = "general")
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Primero obtener los datos actuales
            string selectQuery = $"SELECT * FROM {table} WHERE {column} = @Id";
            Dictionary<string, object> originalData = new Dictionary<string, object>();

            await using var connection = await OpenConnectionAsync();

            await using var selectCommand = new SqlCommand(selectQuery, connection);
            selectCommand.Parameters.AddWithValue("@Id", id);

            await using var reader = await selectCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    originalData[reader.GetName(i)] = reader.GetValue(i);
                }
            }
            await reader.CloseAsync();

            if (originalData.Count == 0)
                return NotFound(new { Message = "Registro no encontrado" });

            // Realizar la actualización
            string updateQuery = $"UPDATE {table} SET estado = 'archivado' WHERE {column} = @Id";
            await using var updateCommand = new SqlCommand(updateQuery, connection);
            updateCommand.Parameters.AddWithValue("@Id", id);

            var result = await updateCommand.ExecuteNonQueryAsync();

            if (result > 0)
            {
                // Notificar a todos los clientes sobre el archivado
                await _hubContext.Clients.Group("PedidosGeneral")
                    .SendAsync("RegistroArchivado", new
                    {
                        Tabla = table,
                        RegistroId = id,
                        DatosOriginales = originalData,
                        Accion = "Archive",
                        UsuarioId = userId,
                        Timestamp = DateTime.UtcNow
                    });

                _memoryCache.Remove($"general_{table}_{id}");
                _memoryCache.Remove($"general_all_{table}");
                await _authUtils.InsertUserHistory(userId, "general delete", $"Archivado de documento en {table} con ID {id}");
                return Ok(new
                {
                    Message = "Registro archivado exitosamente",
                    Data = originalData
                });
            }

            return NotFound(new { Message = "Registro no encontrado" });
        }

        // ✅ Eliminación física - CORREGIDO: Devuelve los datos antes de eliminar
        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> Eliminar(int id, [FromQuery] string? column = "id", [FromQuery] string? table = "general")
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Primero obtener los datos actuales
            string selectQuery = $"SELECT * FROM {table} WHERE {column} = @Id";
            Dictionary<string, object> originalData = new Dictionary<string, object>();

            await using var connection = await OpenConnectionAsync();

            // Obtener datos originales
            await using var selectCommand = new SqlCommand(selectQuery, connection);
            selectCommand.Parameters.AddWithValue("@Id", id);

            await using var reader = await selectCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    originalData[reader.GetName(i)] = reader.GetValue(i);
                }
            }
            await reader.CloseAsync();

            if (originalData.Count == 0)
                return NotFound(new { Message = "Registro no encontrado" });

            // Realizar la eliminación
            string deleteQuery = $"DELETE FROM {table} WHERE {column} = @Id";
            await using var deleteCommand = new SqlCommand(deleteQuery, connection);
            deleteCommand.Parameters.AddWithValue("@Id", id);

            var result = await deleteCommand.ExecuteNonQueryAsync();

            if (result > 0)
            {
                // Notificar a todos los clientes sobre el eliminado
                await _hubContext.Clients.Group("PedidosGeneral")
                    .SendAsync("RegistroEliminado", new
                    {
                        Tabla = table,
                        RegistroId = id,
                        DatosOriginales = originalData,
                        Accion = "Delete",
                        UsuarioId = userId,
                        Timestamp = DateTime.UtcNow
                    });

                await _authUtils.InsertUserHistory(userId, "general delete", $"Eliminación en {table} con ID {id}");
                _memoryCache.Remove($"general_{table}_{id}");
                _memoryCache.Remove($"general_all_{table}");
                return Ok(new
                {
                    Message = "Registro eliminado exitosamente",
                    Data = originalData
                });
            }

            return NotFound(new { Message = "Registro no encontrado" });
        }
    }
}