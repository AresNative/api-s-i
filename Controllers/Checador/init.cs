using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers.checador
{
    [ApiExplorerSettings(GroupName = "checador")]
    [Route("api/v1/checador")]
    [ApiController]
    public partial class ChecadorController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;

        public ChecadorController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils)
            : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        // ✅ Consulta checador (sin ID)
        [Authorize]
        [HttpGet("consultar")]
        public async Task<IActionResult> ConsultarChecador()
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string cacheKey = $"checador_all";
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
                return Ok(cachedResults);

            string query = $"SELECT * FROM checador";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);

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

            await _authUtils.InsertUserHistory(userId, "checador load all", $"Consulta de todos los registros en checador");
            return Ok(results);
        }

        // ✅ Consulta por ID
        [Authorize]
        [HttpGet("consultar/{id}")]
        public async Task<IActionResult> ConsultarPorId(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string cacheKey = $"checador_{id}";
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
                return Ok(cachedResults);

            string query = $"SELECT * FROM checador WHERE id = @ID";

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
            await _authUtils.InsertUserHistory(userId, "checador load by id", $"Consulta en checador con ID {id}");

            return Ok(results);
        }

        // ✅ Registro dinámico con JSON
        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> Registrar([FromBody] JObject data)
        {
            if (data == null) return BadRequest(new { Message = "JSON inválido" });

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }


            // 🔑 Construcción dinámica de columnas y parámetros
            var columnNames = string.Join(", ", data.Properties().Select(p => $"[{p.Name}]"));
            var parameterNames = string.Join(", ", data.Properties().Select(p => $"@{p.Name}"));

            var query = $@"
            INSERT INTO [checador] ({columnNames})
            OUTPUT INSERTED.id
            VALUES ({parameterNames});";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);

            foreach (var prop in data.Properties())
                command.Parameters.AddWithValue("@" + prop.Name, prop.Value?.ToObject<object>() ?? DBNull.Value);

            var insertedId = await command.ExecuteScalarAsync();

            if (insertedId != null)
            {
                await _authUtils.InsertUserHistory(userId, "checador insert", $"Registro en checador con ID {insertedId}");
                _memoryCache.Remove($"checador_all");
            }

            return Ok(new { Message = "Registro exitoso", Id = insertedId });
        }

        // ✅ Actualización dinámica
        [Authorize]
        [HttpPut("update/{id}")]
        public async Task<IActionResult> Actualizar(int id, [FromBody] JObject data)
        {
            if (data == null) return BadRequest(new { Message = "JSON inválido" });

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var setClause = string.Join(",", data.Properties().Select(p => $"{p.Name} = @{p.Name}"));
            string query = $"UPDATE checador SET {setClause} WHERE id = @Id";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            foreach (var prop in data.Properties())
                command.Parameters.AddWithValue("@" + prop.Name, prop.Value?.ToObject<object>() ?? DBNull.Value);

            var result = await command.ExecuteNonQueryAsync();

            if (result > 0)
            {
                await _authUtils.InsertUserHistory(userId, "checador update", $"Actualización en checador con ID {id}");
                _memoryCache.Remove($"checador_{id}");
                _memoryCache.Remove($"checador_all");
                return Ok(new { Message = "Actualización exitosa" });
            }

            return NotFound(new { Message = "Registro no encontrado" });
        }

        // ✅ Eliminación lógica
        [Authorize]
        [HttpDelete("archivar/{id}")]
        public async Task<IActionResult> Archivar(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string query = $"UPDATE checador SET estado = 'archivado' WHERE id = @Id";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            var result = await command.ExecuteNonQueryAsync();

            if (result > 0)
            {
                await _authUtils.InsertUserHistory(userId, "checador delete", $"Archivado de documento en checador con ID {id}");
                _memoryCache.Remove($"checador_{id}");
                _memoryCache.Remove($"checador_all");
                return Ok(new { Message = "Registro eliminado exitosamente" });
            }

            return NotFound(new { Message = "Registro no encontrado" });
        }
        // ✅ Eliminación lógica
        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string query = $"DELETE checador WHERE id = @Id";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            var result = await command.ExecuteNonQueryAsync();

            if (result > 0)
            {
                await _authUtils.InsertUserHistory(userId, "checador delete", $"Eliminación en checador con ID {id}");
                _memoryCache.Remove($"checador_{id}");
                _memoryCache.Remove($"checador_all");
                return Ok(new { Message = "Registro eliminado exitosamente" });
            }

            return NotFound(new { Message = "Registro no encontrado" });
        }
    }
}
