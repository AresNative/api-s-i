using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Caching.Memory;
using MyApiProject.Models;

namespace MyApiProject.Controllers.pickup
{
    [ApiExplorerSettings(GroupName = "pickup")]
    [Route("api/v1/pickup/listas")]
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
        [HttpGet("consultar")]
        public async Task<IActionResult> ConsultarListas()
        {
            string cacheKey = $"listas_all";
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
                return Ok(cachedResults);

            string query = $"SELECT * FROM listas";

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

            return Ok(results);
        }

        // ✅ Consulta por ID
        [HttpGet("consultar/{id}")]
        public async Task<IActionResult> ConsultarPorId(int id)
        {
            string cacheKey = $"listas_{id}";
            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
                return Ok(cachedResults);

            string query = $"SELECT * FROM listas WHERE id = @ID";

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
            return Ok(results);
        }

        [HttpPost("consultar/filtros")]
        public async Task<IActionResult> ConsultarListasConFiltros(
            [FromBody] FiltrosRequest request,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;

            int offset = (page - 1) * pageSize;

            var baseQuery = @"FROM listas";
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
                return StatusCode(500, new { Message = "Error interno del servidor", Details = ex.Message });
            }
        }
        // ✅ Registro dinámico con JSON
        [HttpPost("register")]
        public async Task<IActionResult> Registrar([FromBody] JObject data)
        {
            if (data == null) return BadRequest(new { Message = "JSON inválido" });
            // 🔑 Construcción dinámica de columnas y parámetros
            var columnNames = string.Join(", ", data.Properties().Select(p => $"[{p.Name}]"));
            var parameterNames = string.Join(", ", data.Properties().Select(p => $"@{p.Name}"));

            var query = $@"
            INSERT INTO [listas] ({columnNames})
            OUTPUT INSERTED.*
            VALUES ({parameterNames});";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);

            foreach (var prop in data.Properties())
                command.Parameters.AddWithValue("@" + prop.Name, prop.Value?.ToObject<object>() ?? DBNull.Value);

            // Ejecutar y obtener todos los datos insertados
            await using var reader = await command.ExecuteReaderAsync();
            var insertedData = new Dictionary<string, object>();

            if (await reader.ReadAsync())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    insertedData[reader.GetName(i)] = reader.GetValue(i);
                }
            }

            if (insertedData.Count > 0)
            {
                _memoryCache.Remove($"listas_all");
                return Ok(new { Message = "Registro exitoso", Data = insertedData });
            }

            return StatusCode(500, new { Message = "Error al insertar el registro" });
        }

        // ✅ Actualización dinámica
        [HttpPut("update/{id}")]
        public async Task<IActionResult> Actualizar(int id, [FromBody] JObject data)
        {
            if (data == null) return BadRequest(new { Message = "JSON inválido" });



            var setClause = string.Join(",", data.Properties().Select(p => $"{p.Name} = @{p.Name}"));
            string query = $"UPDATE listas SET {setClause} WHERE id = @Id";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            foreach (var prop in data.Properties())
                command.Parameters.AddWithValue("@" + prop.Name, prop.Value?.ToObject<object>() ?? DBNull.Value);

            var result = await command.ExecuteNonQueryAsync();

            if (result > 0)
            {
                _memoryCache.Remove($"listas_{id}");
                _memoryCache.Remove($"listas_all");
                return Ok(new { Message = "Actualización exitosa" });
            }

            return NotFound(new { Message = "Registro no encontrado" });
        }

        // ✅ Eliminación lógica
        [HttpDelete("archivar/{id}")]
        public async Task<IActionResult> Archivar(int id)
        {


            string query = $"UPDATE listas SET estado = 'archivado' WHERE id = @Id";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            var result = await command.ExecuteNonQueryAsync();

            if (result > 0)
            {
                _memoryCache.Remove($"listas_{id}");
                _memoryCache.Remove($"listas_all");
                return Ok(new { Message = "Registro eliminado exitosamente" });
            }

            return NotFound(new { Message = "Registro no encontrado" });
        }
        // ✅ Eliminación lógica
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            string query = $"DELETE listas WHERE id = @Id";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            var result = await command.ExecuteNonQueryAsync();

            if (result > 0)
            {
                _memoryCache.Remove($"listas_{id}");
                _memoryCache.Remove($"listas_all");
                return Ok(new { Message = "Registro eliminado exitosamente" });
            }

            return NotFound(new { Message = "Registro no encontrado" });
        }
    }
}
