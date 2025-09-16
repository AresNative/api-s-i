using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Caching.Memory;
using MyApiProject.Models;

namespace MyApiProject.Controllers.i_sync
{
    [ApiExplorerSettings(GroupName = "i_sync")]
    [Route("api/v1/listas")]
    [ApiController]
    public partial class ListasController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;

        public ListasController(IConfiguration configuration, IMemoryCache memoryCache, AuthUtils authUtils) : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        // ✅ Consulta listas (sin ID)
        [Authorize]
        [HttpGet("consultar")]
        public async Task<IActionResult> ConsultarListas()
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string cacheKey = $"listas_all";
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
                return Ok(cachedResults);

            string query = $"SELECT * FROM [TC032841E].[dbo].listas";

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

            await _authUtils.InsertUserHistory(userId, "listas load all", $"Consulta de todos los registros en listas");
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

            string cacheKey = $"listas_{id}";
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
                return Ok(cachedResults);

            string query = $"SELECT * FROM [TC032841E].[dbo].listas WHERE id = @ID";

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
            await _authUtils.InsertUserHistory(userId, "listas load by id", $"Consulta en listas con ID {id}");

            return Ok(results);
        }

        [Authorize]
        [HttpPost("consultar/filtros")]
        public async Task<IActionResult> ConsultarListasConFiltros(
            [FromBody] FiltrosRequest request,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;

            int offset = (page - 1) * pageSize;

            var baseQuery = @"FROM [TC032841E].[dbo].listas";
            var whereClauses = new List<string>();
            var parameters = new List<SqlParameter>();
            var parameterCounters = new Dictionary<string, int>();

            // Procesar filtros usando el método BuildFilters
            BuildFilters(request, whereClauses, parameters, parameterCounters);

            // Agrupar condiciones para el mismo campo con OR
            var groupedWhereClauses = AgruparCondiciones(whereClauses);

            var whereQuery = groupedWhereClauses.Any()
                ? $"WHERE {string.Join(" AND ", groupedWhereClauses)}"
                : "";

            var countQuery = $@"SELECT COUNT(*) AS TotalRegistros {baseQuery} {whereQuery}";

            var paginatedQuery = $@"
                SELECT *
                {baseQuery} {whereQuery}
                ORDER BY id
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            try
            {
                await using var connection = await OpenConnectionAsync();

                // Total records
                var countCommandParameters = parameters
                    .Select(p => new SqlParameter(p.ParameterName, p.Value))
                    .ToList();

                await using var countCommand = new SqlCommand(countQuery, connection);
                countCommand.Parameters.AddRange(countCommandParameters.ToArray());
                var totalRecords = (int)await countCommand.ExecuteScalarAsync();

                // Paginated data
                var paginatedParameters = parameters
                    .Select(p => new SqlParameter(p.ParameterName, p.Value))
                    .ToList();

                paginatedParameters.AddRange(new[]
                {
                    new SqlParameter("@Offset", offset),
                    new SqlParameter("@PageSize", pageSize)
                });

                await using var command = new SqlCommand(paginatedQuery, connection);
                command.Parameters.AddRange(paginatedParameters.ToArray());

                var results = new List<Dictionary<string, object>>();

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }
                    results.Add(row);
                }

                // Crear clave de caché única basada en los filtros
                var filtrosCacheKey = string.Join("_", request.Filtros
                    .Where(f => !string.IsNullOrWhiteSpace(f.Value))
                    .Select(f => $"{f.Key}_{f.Value}_{f.Operator}"));

                var cacheKey = $"listas_filtros_{filtrosCacheKey}_page{page}_size{pageSize}";
                _memoryCache.Set(cacheKey, results, TimeSpan.FromMinutes(5));

                await _authUtils.InsertUserHistory(userId, "listas load with filters",
                    $"Consulta en listas con {request.Filtros.Count} filtros, página {page}, tamaño {pageSize}");

                return Ok(new
                {
                    TotalRecords = totalRecords,
                    TotalPages = (int)Math.Ceiling(totalRecords / (double)pageSize),
                    PageSize = pageSize,
                    Page = page,
                    Data = results
                });
            }
            catch (Exception ex)
            {
                await _authUtils.InsertUserHistory(userId, "listas error",
                    $"Error en consulta con filtros: {ex.Message}");
                return StatusCode(500, new { Message = "Error interno del servidor", Details = ex.Message });
            }
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
            INSERT INTO [TC032841E].[dbo].[listas] ({columnNames})
            OUTPUT INSERTED.id
            VALUES ({parameterNames});";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);

            foreach (var prop in data.Properties())
                command.Parameters.AddWithValue("@" + prop.Name, prop.Value?.ToObject<object>() ?? DBNull.Value);

            var insertedId = await command.ExecuteScalarAsync();

            if (insertedId != null)
            {
                await _authUtils.InsertUserHistory(userId, "listas insert", $"Registro en listas con ID {insertedId}");
                _memoryCache.Remove($"listas_all");
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
            string query = $"UPDATE [TC032841E].[dbo].listas SET {setClause} WHERE id = @Id";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            foreach (var prop in data.Properties())
                command.Parameters.AddWithValue("@" + prop.Name, prop.Value?.ToObject<object>() ?? DBNull.Value);

            var result = await command.ExecuteNonQueryAsync();

            if (result > 0)
            {
                await _authUtils.InsertUserHistory(userId, "listas update", $"Actualización en listas con ID {id}");
                _memoryCache.Remove($"listas_{id}");
                _memoryCache.Remove($"listas_all");
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

            string query = $"UPDATE [TC032841E].[dbo].listas SET estado = 'archivado' WHERE id = @Id";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            var result = await command.ExecuteNonQueryAsync();

            if (result > 0)
            {
                await _authUtils.InsertUserHistory(userId, "listas delete", $"Archivado de documento en listas con ID {id}");
                _memoryCache.Remove($"listas_{id}");
                _memoryCache.Remove($"listas_all");
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

            string query = $"DELETE [TC032841E].[dbo].listas WHERE id = @Id";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            var result = await command.ExecuteNonQueryAsync();

            if (result > 0)
            {
                await _authUtils.InsertUserHistory(userId, "listas delete", $"Eliminación en listas con ID {id}");
                _memoryCache.Remove($"listas_{id}");
                _memoryCache.Remove($"listas_all");
                return Ok(new { Message = "Registro eliminado exitosamente" });
            }

            return NotFound(new { Message = "Registro no encontrado" });
        }
    }
}
