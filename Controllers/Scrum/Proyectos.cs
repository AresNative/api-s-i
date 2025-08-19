using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers.Scrum
{

    [ApiExplorerSettings(GroupName = "scrum")]
    [Route("api/v1/proyectos")]
    [ApiController]
    public partial class ProyectosController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;
        public ProyectosController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils)
        : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        [Authorize]
        [HttpGet("consultar")]
        public async Task<IActionResult> ConsultarProyects()
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Clave única para cachear resultados de un sprint específico
            string cacheKey = $"proyecto";

            // Si existe en cache, devolverlo
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT * FROM proyectos";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);

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
                "scrum load projects",
                $"Consulta de proyectos"
            );

            return Ok(results);
        }
        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegistrarProyectos([FromBody] Proyectos nuevoProyectos)
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del proyectos." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum upload task", "Registro de proyectos -> " + nuevoProyectos.nombre);
            return await InsertJsonToDatabaseAsync(
                data: nuevoProyectos,
                tableName: "proyectos"
            );
        }

        [Authorize]
        [HttpPut("update/{id}")]
        public async Task<IActionResult> ActualizarProyectos(int id, [FromBody] ProyectosUpdate proyectosActualizado)
        {
            if (proyectosActualizado == null)
                return BadRequest(new { Message = "Datos de proyectos inválidos." });

            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del proyectos." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum update task", "Actualización de proyectos con ID " + id);

            return await UpdateJsonInDatabaseAsync(
                data: proyectosActualizado,
                tableName: "proyectos",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM proyectos WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Proyectos no encontrado." }) : null;
                }
            );
        }

        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> EliminarProyectos(int id)
        {
            string query = @"DELETE FROM proyectos WHERE Id = @Id";

            try
            {
                await using var connection = await OpenConnectionAsync();
                await using var command = new SqlCommand(query, connection);

                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();

                if (result > 0)
                    return Ok(new { Message = "Proyectos eliminado exitosamente" });
                else
                    return NotFound(new { Message = "Proyectos no encontrado" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
