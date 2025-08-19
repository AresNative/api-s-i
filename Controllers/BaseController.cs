// Controllers/BaseController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace MyApiProject.Controllers
{
    public abstract class BaseController : ControllerBase
    {
        private readonly string _connectionString;
        protected readonly IMemoryCache _cache;

        public BaseController(IConfiguration configuration, IMemoryCache cache)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException("Cadena de conexión 'DefaultConnection' no encontrada");
            _cache = cache;
        }

        protected async Task<SqlConnection> OpenConnectionAsync()
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }

        protected IActionResult HandleException(Exception ex, string? query = null)
        {
            if (ex is OperationCanceledException || ex is TaskCanceledException)
                return StatusCode(499, new { Message = "Solicitud cancelada por el cliente" });

            var sanitizedMessage = ex.Message.Replace("\r", "").Replace("\n", " ");
            var sanitizedQuery = query?.Replace("\r", "").Replace("\n", " ");

            return StatusCode(500, new
            {
                Message = $"Error: {sanitizedMessage}",
                Query = sanitizedQuery
            });
        }

        protected IActionResult HandleException(Exception ex, int statusCode)
        {
            if (ex is OperationCanceledException || ex is TaskCanceledException)
                return StatusCode(499, "Solicitud cancelada por el cliente");

            return StatusCode(statusCode, new { Message = $"Error: {ex.Message}" });
        }
        protected int GetUserIdFromToken()
        {
            var userIdClaim = User?.Claims?.FirstOrDefault(c => c.Type == "userId");

            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }

            return 0; // Valor por defecto si no se encuentra el claim
        }
        protected int ObtenerUsuarioId()
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
                throw new UnauthorizedAccessException("Token no válido o no se pudo extraer el ID del usuario.");
            return userId;
        }

        protected static int? ExtraerIdDeResultado(IActionResult result)
        {
            if (result is OkObjectResult ok && ok.Value is not null)
            {
                var resultData = JsonConvert.DeserializeObject<dynamic>(
                    JsonConvert.SerializeObject(ok.Value)
                );
                return (int?)resultData?.Id;
            }
            return null;
        }
        protected async Task<IActionResult> InsertJsonToDatabaseAsync<T>(
            T data,
            string tableName,
            IFormFile? file = null,
            string fileColumn = "file",
            Dictionary<string, object>? extraColumns = null,
            Func<SqlConnection, Task<IActionResult>>? preValidation = null
        )
        {
            try
            {
                // Guardar archivo si existe
                string? filePath = null;
                if (file != null)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
                    Directory.CreateDirectory(uploadsFolder);

                    var fileExtension = Path.GetExtension(file.FileName);
                    var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                    filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    await using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);
                }

                await using var connection = await OpenConnectionAsync();

                if (preValidation != null)
                {
                    var validationResult = await preValidation(connection);
                    if (validationResult != null) return validationResult;
                }

                // Obtener propiedades del modelo
                var properties = typeof(T).GetProperties()
                    .Where(p => p.GetValue(data) != null)
                    .ToList();

                var allColumns = new List<string>();
                var allParameters = new List<string>();
                var sqlParameters = new List<SqlParameter>();

                foreach (var prop in properties)
                {
                    allColumns.Add($"[{prop.Name}]");
                    allParameters.Add($"@{prop.Name}");
                    sqlParameters.Add(new SqlParameter($"@{prop.Name}", prop.GetValue(data) ?? DBNull.Value));
                }

                // Extra columns (manuales)
                if (extraColumns != null)
                {
                    foreach (var entry in extraColumns)
                    {
                        allColumns.Add($"[{entry.Key}]");
                        allParameters.Add($"@{entry.Key}");
                        sqlParameters.Add(new SqlParameter($"@{entry.Key}", entry.Value ?? DBNull.Value));
                    }
                }

                // Archivo (si se incluye)
                if (filePath != null)
                {
                    allColumns.Add($"[{fileColumn}]");
                    allParameters.Add("@FilePath");
                    sqlParameters.Add(new SqlParameter("@FilePath", filePath));
                }

                var query = $@"
            INSERT INTO [{tableName}] ({string.Join(", ", allColumns)})
            OUTPUT INSERTED.ID
            VALUES ({string.Join(", ", allParameters)});
        ";

                await using var command = new SqlCommand(query, connection);
                command.Parameters.AddRange(sqlParameters.ToArray());

                var insertedId = await command.ExecuteScalarAsync();

                return Ok(new { Message = $"{tableName} insertado correctamente.", Id = insertedId });
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Error al insertar en {tableName}.");
            }
        }
        protected async Task<IActionResult> UpdateJsonInDatabaseAsync<T>(
            T data,
            string tableName,
            string keyColumn,
            object keyValue,
            Func<SqlConnection, Task<IActionResult?>>? preValidation = null
        )
        {
            try
            {
                await using var connection = await OpenConnectionAsync();

                if (preValidation != null)
                {
                    var validationResult = await preValidation(connection);
                    if (validationResult != null) return validationResult;
                }

                // Obtener propiedades con valores no nulos, excluyendo la clave primaria
                var properties = typeof(T).GetProperties()
                    .Where(p => p.Name.ToLower() != keyColumn.ToLower())
                    .Where(p => p.GetValue(data) != null)
                    .ToList();

                if (!properties.Any())
                {
                    return BadRequest(new { Message = "No se proporcionaron campos para actualizar." });
                }

                var setClause = string.Join(", ", properties.Select(p => $"{p.Name} = @{p.Name}"));
                var query = $"UPDATE {tableName} SET {setClause} WHERE {keyColumn} = @KeyValue";

                await using var command = new SqlCommand(query, connection);

                foreach (var prop in properties)
                {
                    var value = prop.GetValue(data);
                    command.Parameters.AddWithValue($"@{prop.Name}", value);
                }

                command.Parameters.AddWithValue("@KeyValue", keyValue);

                var result = await command.ExecuteNonQueryAsync();

                return result > 0
                    ? Ok(new { Message = "Información actualizada correctamente." })
                    : NotFound(new { Message = "Información no encontrada." });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
