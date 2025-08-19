using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers.subasta
{
    [ApiExplorerSettings(GroupName = "subasta")]
    [Route("api/v1/subasta_compras")]
    [ApiController]
    public partial class Subasta_ComprasController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;

        public Subasta_ComprasController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils)
            : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        [Authorize]
        [HttpGet("consultar/{sprint_id}")]
        public async Task<IActionResult> ConsultarTarea(int sprint_id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Clave única para cachear resultados de un sprint específico
            string cacheKey = $"subasta_compras_sprint_{sprint_id}";

            // Si existe en cache, devolverlo
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT * FROM subasta_compras WHERE sprint_id = @SprintId";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SprintId", sprint_id);

            await using var reader = await command.ExecuteReaderAsync();
            var results = new List<Dictionary<string, object>>();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.GetValue(i);
                }
                results.Add(row);
            }

            // Guardar en caché por 5 minutos
            _memoryCache.Set(cacheKey, results, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

            return Ok(results);
        }

        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegistrarTarea([FromBody] Tarea nuevoTarea)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var insertResult = await InsertJsonToDatabaseAsync(
                data: nuevoTarea,
                tableName: "subasta_compras"
            );

            var tareaId = ExtraerIdDeResultado(insertResult);
            if (tareaId.HasValue && tareaId.Value > 0)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "scrum upload task",
                    $"Registro de tarea -> {nuevoTarea.titulo}"
                );
                // Limpiar cache relacionado con subasta_compras
                _memoryCache.Remove($"subasta_compras_sprint_{nuevoTarea.sprint_id}");
            }

            return insertResult;
        }

        [Authorize]
        [HttpPut("update/{id}")]
        public async Task<IActionResult> ActualizarTarea(int id, [FromBody] TareaUpdate tareaActualizado)
        {
            if (tareaActualizado == null)
                return BadRequest(new { Message = "Datos de tarea inválidos." });

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var updateResult = await UpdateJsonInDatabaseAsync(
                data: tareaActualizado,
                tableName: "subasta_compras",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM subasta_compras WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Tarea no encontrada." }) : null;
                }
            );

            if (updateResult is OkObjectResult)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "scrum update task",
                    $"Actualización de tarea con ID {id}"
                );

                // Invalidar todas las posibles cachés de subasta_compras
                if (tareaActualizado.sprint_id.HasValue)
                    _memoryCache.Remove($"subasta_compras_sprint_{tareaActualizado.sprint_id.Value}");
            }

            return updateResult;
        }

        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> EliminarTarea(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string query = @"UPDATE subasta_compras SET estado = 'archivado' WHERE Id = @Id";

            try
            {
                await using var connection = await OpenConnectionAsync();
                await using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();

                if (result > 0)
                {
                    await _authUtils.InsertUserHistory(
                        userId,
                        "scrum delete task",
                        $"Eliminación de tarea con ID {id}"
                    );

                    // Si quieres invalidar todas las cachés de subasta_compras, puedes usar este patrón:
                    // _memoryCache.Remove("subasta_compras_sprint_" + sprint_id); // Si conoces el sprint
                    // O invalidar globalmente si guardas una lista de claves

                    return Ok(new { Message = "Tarea eliminada exitosamente" });
                }
                else
                {
                    return NotFound(new { Message = "Tarea no encontrada" });
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
