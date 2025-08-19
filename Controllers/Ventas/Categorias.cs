using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers.ventas
{
    [ApiExplorerSettings(GroupName = "ventas")]
    [Route("api/v1/categorias")]
    [ApiController]
    public partial class CategoriasController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;

        public CategoriasController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils)
            : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        [Authorize]
        [HttpGet("consultar/{id}")]
        public async Task<IActionResult> ConsultarCategoria(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Clave única para cachear resultados de un categoria específico
            string cacheKey = $"categorias_{id}";

            // Si existe en cache, devolverlo
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT * FROM categorias WHERE id = @ID";

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
                "ventas load categoria",
                $"Consulta de categorias con el ID {id}"
            );

            return Ok(results);
        }

        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegistrarCategoria([FromBody] Categoria nuevo_categoria)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var insertResult = await InsertJsonToDatabaseAsync(
                data: nuevo_categoria,
                tableName: "categorias"
            );

            var categoriaId = ExtraerIdDeResultado(insertResult);
            if (categoriaId.HasValue && categoriaId.Value > 0)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "ventas upload categoria",
                    $"Registro de categoria con ID {categoriaId}"
                );

                // Limpiar cache relacionado con categorias
                _memoryCache.Remove($"categorias_{categoriaId}");
            }

            return insertResult;
        }

        [Authorize]
        [HttpPut("update/{id}")]
        public async Task<IActionResult> ActualizarCategoria(int id, [FromBody] CategoriaUpdate categoriaActualizado)
        {
            if (categoriaActualizado == null)
                return BadRequest(new { Message = "Datos de categoria inválidos." });

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var updateResult = await UpdateJsonInDatabaseAsync(
                data: categoriaActualizado,
                tableName: "categorias",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM categorias WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Categoria no encontrada." }) : null;
                }
            );

            if (updateResult is OkObjectResult)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "ventas update categoria",
                    $"Actualización de categoria con ID {id}"
                );

                // Invalidar todas las posibles cachés de categorias
                if (categoriaActualizado.id.HasValue)
                    _memoryCache.Remove($"categorias_{categoriaActualizado.id.Value}");
            }

            return updateResult;
        }

        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> EliminarCategoria(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string query = @"UPDATE categorias SET estado = 'archivado' WHERE Id = @Id";

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
                        "ventas delete categoria",
                        $"Eliminación de categoria con ID {id}"
                    );
                    // Si quieres invalidar todas las cachés de categorias, puedes usar este patrón:
                    // _memoryCache.Remove("categorias_" + id); // Si conoces el categoria
                    // O invalidar globalmente si guardas una lista de claves

                    return Ok(new { Message = "Categoria eliminada exitosamente" });
                }
                else
                {
                    return NotFound(new { Message = "Categoria no encontrada" });
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
