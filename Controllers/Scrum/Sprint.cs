using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers
{
    public partial class SprintsController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;
        public SprintsController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils)
        : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }
        private int ObtenerUsuarioId()
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
                throw new UnauthorizedAccessException("Token no válido o no se pudo extraer el ID del usuario.");
            return userId;
        }

        [Authorize]
        [HttpGet("api/v1/sprints/consultar/{proyecto_id}")]
        public async Task<IActionResult> ConsultarSprints(int proyecto_id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Clave única para cachear resultados de un sprint específico
            string cacheKey = $"sprints_proyecto_{proyecto_id}";

            // Si existe en cache, devolverlo
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT * FROM sprints WHERE proyecto_id = @ProyectoId";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@ProyectoId", proyecto_id);

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
                "scrum load sprints",
                $"Consulta de sprints para el sprint ID {proyecto_id}"
            );

            return Ok(results);
        }

        [Authorize]
        [HttpPost("api/v1/sprints/register")]
        public async Task<IActionResult> RegistrarSprints([FromBody] Sprints nuevoSprints)
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del sprints." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum upload task", "Registro de sprints -> " + nuevoSprints.nombre);

            return await InsertJsonToDatabaseAsync(
                data: nuevoSprints,
                tableName: "sprints"
            );
        }

        [Authorize]
        [HttpPut("api/v1/sprints/update/{id}")]
        public async Task<IActionResult> ActualizarSprints(int id, [FromBody] SprintsUpdate sprintsActualizado)
        {
            if (sprintsActualizado == null)
                return BadRequest(new { Message = "Datos de sprints inválidos." });

            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del sprints." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum update task", "Actualización de sprints con ID " + id);

            return await UpdateJsonInDatabaseAsync(
                data: sprintsActualizado,
                tableName: "sprints",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM sprints WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Sprints no encontrado." }) : null;
                }
            );
        }

        [Authorize]
        [HttpDelete("api/v1/sprints/delete/{id}")]
        public async Task<IActionResult> EliminarSprints(int id)
        {
            string query = @"DELETE FROM sprints WHERE Id = @Id";

            try
            {
                await using var connection = await OpenConnectionAsync();
                await using var command = new SqlCommand(query, connection);

                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();

                if (result > 0)
                    return Ok(new { Message = "Sprints eliminado exitosamente" });
                else
                    return NotFound(new { Message = "Sprints no encontrado" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
