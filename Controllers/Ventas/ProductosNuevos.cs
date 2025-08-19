using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers.ventas
{
    [ApiExplorerSettings(GroupName = "ventas")]
    [Route("api/v1/productos_nuevos")]
    [ApiController]
    public partial class Productos_NuevosController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;

        public Productos_NuevosController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils)
            : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        [Authorize]
        [HttpGet("consultar/{id}")]
        public async Task<IActionResult> ConsultarProductos_nuevos(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Clave única para cachear resultados de un producto específico
            string cacheKey = $"productos_nuevos_{id}";

            // Si existe en cache, devolverlo
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT * FROM productos_nuevos WHERE id = @ID";

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
                "ventas load productos_nuevos",
                $"Consulta de productos_nuevos con el ID {id}"
            );

            return Ok(results);
        }

        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegistrarProductos_nuevos([FromBody] Productos_nuevos nuevo_productos_nuevos)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var insertResult = await InsertJsonToDatabaseAsync(
                data: nuevo_productos_nuevos,
                tableName: "productos_nuevos"
            );

            var productos_nuevosId = ExtraerIdDeResultado(insertResult);
            if (productos_nuevosId.HasValue && productos_nuevosId.Value > 0)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "ventas upload productos_nuevos",
                    $"Registro de productos_nuevos con ID {productos_nuevosId}"
                );

                // Limpiar cache relacionado con productos_Nuevos
                _memoryCache.Remove($"productos_Nuevos_{productos_nuevosId}");
            }

            return insertResult;
        }

        [Authorize]
        [HttpPut("update/{id}")]
        public async Task<IActionResult> ActualizarProductos_nuevos(int id, [FromBody] Productos_nuevosUpdate productos_nuevosActualizado)
        {
            if (productos_nuevosActualizado == null)
                return BadRequest(new { Message = "Datos de productos_nuevos inválidos." });

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var updateResult = await UpdateJsonInDatabaseAsync(
                data: productos_nuevosActualizado,
                tableName: "productos_nuevos",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM productos_nuevos WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Productos_nuevos no encontrada." }) : null;
                }
            );

            if (updateResult is OkObjectResult)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "ventas update productos_nuevos",
                    $"Actualización de productos_nuevos con ID {id}"
                );

                // Invalidar todas las posibles cachés de productos_Nuevos
                if (productos_nuevosActualizado.id.HasValue)
                    _memoryCache.Remove($"productos_nuevos_{productos_nuevosActualizado.id.Value}");
            }

            return updateResult;
        }

        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> EliminarProductos_nuevos(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string query = @"UPDATE productos_nuevos SET estado = 'archivado' WHERE Id = @Id";

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
                        "ventas delete productos_nuevos",
                        $"Eliminación de productos_nuevos con ID {id}"
                    );
                    // Si quieres invalidar todas las cachés de productos_Nuevos, puedes usar este patrón:
                    // _memoryCache.Remove("productos_Nuevos_" + id); // Si conoces el producto
                    // O invalidar globalmente si guardas una lista de claves

                    return Ok(new { Message = "Productos_nuevos eliminada exitosamente" });
                }
                else
                {
                    return NotFound(new { Message = "Productos_nuevos no encontrada" });
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
