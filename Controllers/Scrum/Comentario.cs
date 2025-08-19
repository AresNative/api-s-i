using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers.Scrum
{
    [ApiExplorerSettings(GroupName = "scrum")]
    [Route("api/v1/comentarios")]
    [ApiController]
    public partial class ComentariosController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;
        public ComentariosController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils)
        : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        [Authorize]
        [HttpGet("consultar/{tareas_id}")]
        public async Task<IActionResult> ConsultarComentarios(int tareas_id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Clave única para cachear resultados de un sprint específico
            string cacheKey = $"comentarios_tarea_{tareas_id}";

            // Si existe en cache, devolverlo
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT * FROM comentarios WHERE tarea_id = @TareaId";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@TareaId", tareas_id);

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

            await _authUtils.InsertUserHistory(
                userId,
                "scrum load comments",
                $"Consulta de comentarios para la tarea con ID {tareas_id}"
            );

            return Ok(results);
        }

        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegistrarComentarios([FromBody] Comentarios nuevoComentarios)
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del comentarios." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum upload task", "Registro de comentarios -> " + nuevoComentarios.contenido);
            return await InsertJsonToDatabaseAsync(
                data: nuevoComentarios,
                tableName: "comentarios"
            );
        }

        [Authorize]
        [HttpPut("update/{id}")]
        public async Task<IActionResult> ActualizarComentarios(int id, [FromBody] ComentariosUpdate comentariosActualizado)
        {
            if (comentariosActualizado == null)
                return BadRequest(new { Message = "Datos de comentarios inválidos." });

            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del comentarios." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum update comment", "Actualización de comentarios con ID " + id);

            return await UpdateJsonInDatabaseAsync(
                data: comentariosActualizado,
                tableName: "comentarios",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM comentarios WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Comentarios no encontrado." }) : null;
                }
            );
        }

        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> EliminarComentarios(int id)
        {
            string query = @"DELETE FROM comentarios WHERE Id = @Id";

            try
            {
                await using var connection = await OpenConnectionAsync();
                await using var command = new SqlCommand(query, connection);

                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();

                if (result > 0)
                    return Ok(new { Message = "Comentarios eliminado exitosamente" });
                else
                    return NotFound(new { Message = "Comentarios no encontrado" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
