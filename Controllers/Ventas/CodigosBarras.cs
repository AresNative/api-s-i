using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers.ventas
{
    [ApiExplorerSettings(GroupName = "ventas")]
    [Route("api/v1/codigos_barras")]
    [ApiController]
    public partial class Codigos_BarrasController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;

        public Codigos_BarrasController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils)
            : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        [Authorize]
        [HttpGet("consultar/{id}")]
        public async Task<IActionResult> ConsultarCodigos_Barras(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Clave única para cachear resultados de un codigo específico
            string cacheKey = $"codigos_barras_{id}";

            // Si existe en cache, devolverlo
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT * FROM codigos_barras WHERE id = @ID";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@ID", id);

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
                "ventas load codigos_barras",
                $"Consulta de codigos_Barras con el ID {id}"
            );

            return Ok(results);
        }

        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegistrarCodigos_Barras([FromBody] Codigos_Barras nuevo_codigos_Barras)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var insertResult = await InsertJsonToDatabaseAsync(
                data: nuevo_codigos_Barras,
                tableName: "codigos_barras"
            );

            var codigos_BarrasId = ExtraerIdDeResultado(insertResult);
            if (codigos_BarrasId.HasValue && codigos_BarrasId.Value > 0)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "ventas upload codigos_barras",
                    $"Registro de codigos_Barras con ID {codigos_BarrasId}"
                );

                // Limpiar cache relacionado con codigos_Barras
                _memoryCache.Remove($"codigos_Barras_{codigos_BarrasId}");
            }

            return insertResult;
        }

        [Authorize]
        [HttpPut("update/{id}")]
        public async Task<IActionResult> ActualizarCodigos_Barras(int id, [FromBody] Codigos_BarrasUpdate codigos_BarrasActualizado)
        {
            if (codigos_BarrasActualizado == null)
                return BadRequest(new { Message = "Datos de codigos_Barras inválidos." });

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var updateResult = await UpdateJsonInDatabaseAsync(
                data: codigos_BarrasActualizado,
                tableName: "codigos_barras",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM codigos_Barras WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Codigos_Barras no encontrada." }) : null;
                }
            );

            if (updateResult is OkObjectResult)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "ventas update codigos_barras",
                    $"Actualización de codigos_barras con ID {id}"
                );

                // Invalidar todas las posibles cachés de codigos_Barras
                if (codigos_BarrasActualizado.id.HasValue)
                    _memoryCache.Remove($"codigos_barras_{codigos_BarrasActualizado.id.Value}");
            }

            return updateResult;
        }

        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> EliminarCodigos_Barras(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string query = @"UPDATE codigos_barras SET estado = 'archivado' WHERE Id = @Id";

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
                        "ventas delete codigos_barras",
                        $"Eliminación de codigos_Barras con ID {id}"
                    );
                    // Si quieres invalidar todas las cachés de codigos_Barras, puedes usar este patrón:
                    // _memoryCache.Remove("codigos_Barras_" + id); // Si conoces el codigo
                    // O invalidar globalmente si guardas una lista de claves

                    return Ok(new { Message = "Codigos_Barras eliminada exitosamente" });
                }
                else
                {
                    return NotFound(new { Message = "Codigos_Barras no encontrada" });
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
