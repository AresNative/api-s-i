using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers.subasta
{
    [ApiExplorerSettings(GroupName = "subasta")]
    [Route("api/v1/ofertas_proveedores")]
    [ApiController]
    public partial class Ofertas_ProveedoresController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;

        public Ofertas_ProveedoresController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils)
            : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        [Authorize]
        [HttpGet("consultar/{subasta_compra_id}")]
        public async Task<IActionResult> ConsultarOfeta_Proveedores(int subasta_compra_id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            // Clave única para cachear resultados de un sprint específico
            string cacheKey = $"ofertas_proveedores_sprint_{subasta_compra_id}";

            // Si existe en cache, devolverlo
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT * FROM ofertas_proveedores WHERE subasta_compra_id = @SprintId";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SprintId", subasta_compra_id);

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
                "scrum load tasks",
                $"Consulta de ofertas_proveedores para el sprint ID {subasta_compra_id}"
            );

            return Ok(results);
        }

        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegistrarOfeta_Proveedores([FromBody] Ofeta_Proveedores nuevoOfeta_Proveedores)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var insertResult = await InsertJsonToDatabaseAsync(
                data: nuevoOfeta_Proveedores,
                tableName: "ofertas_proveedores"
            );

            var ofeta_ProveedoresId = ExtraerIdDeResultado(insertResult);
            if (ofeta_ProveedoresId.HasValue && ofeta_ProveedoresId.Value > 0)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "scrum upload task",
                    $"Registro de ofeta a subasta con ID {nuevoOfeta_Proveedores.subasta_compra_id}"
                );

                // Limpiar cache relacionado con ofertas_proveedores
                _memoryCache.Remove($"ofertas_proveedores_sprint_{nuevoOfeta_Proveedores.subasta_compra_id}");
            }

            return insertResult;
        }

        [Authorize]
        [HttpPut("update/{id}")]
        public async Task<IActionResult> ActualizarOfeta_Proveedores(int id, [FromBody] Ofeta_ProveedoresUpdate ofeta_proveedoresActualizado)
        {
            if (ofeta_proveedoresActualizado == null)
                return BadRequest(new { Message = "Datos de ofeta_Proveedores inválidos." });

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var updateResult = await UpdateJsonInDatabaseAsync(
                data: ofeta_proveedoresActualizado,
                tableName: "ofertas_proveedores",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM ofertas_proveedores WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Ofeta_Proveedores no encontrada." }) : null;
                }
            );

            if (updateResult is OkObjectResult)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "scrum update task",
                    $"Actualización de ofeta con ID {id}"
                );

                // Invalidar todas las posibles cachés de ofertas_proveedores
                if (ofeta_proveedoresActualizado.subasta_compra_id.HasValue)
                    _memoryCache.Remove($"ofertas_proveedores_sprint_{ofeta_proveedoresActualizado.subasta_compra_id.Value}");
            }

            return updateResult;
        }

        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> EliminarOfeta_Proveedores(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string query = @"UPDATE ofertas_proveedores SET estado = 'archivado' WHERE Id = @Id";

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
                        $"Eliminación de ofeta con ID {id}"
                    );
                    // Si quieres invalidar todas las cachés de ofertas_proveedores, puedes usar este patrón:
                    // _memoryCache.Remove("ofertas_proveedores_sprint_" + subasta_compra_id); // Si conoces el sprint
                    // O invalidar globalmente si guardas una lista de claves

                    return Ok(new { Message = "Ofeta eliminada exitosamente" });
                }
                else
                {
                    return NotFound(new { Message = "Ofeta no encontrada" });
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
