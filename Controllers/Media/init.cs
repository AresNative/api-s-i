using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace MyApiProject.Controllers.general
{
    [ApiExplorerSettings(GroupName = "general")]
    [Route("api/v1/recursos")]
    [ApiController]
    public partial class RecursosController : BaseController
    {
        private readonly IMemoryCache _memoryCache;
        private readonly AuthUtils _authUtils;

        public RecursosController(
            IConfiguration configuration,
            IMemoryCache memoryCache,
            AuthUtils authUtils)
            : base(configuration, memoryCache)
        {
            _memoryCache = memoryCache;
            _authUtils = authUtils;
        }

        #region Endpoints para Imágenes

        [Authorize]
        [HttpGet("imagenes/{tabla}/{id_ref}")]
        public async Task<IActionResult> ConsultarImagenes(string tabla, long id_ref)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string cacheKey = $"imagenes_{tabla}_{id_ref}";

            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT id, id_ref, tabla, url, descripcion 
                            FROM imagenes 
                            WHERE id_ref = @IdRef AND tabla = @Tabla";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@IdRef", id_ref);
            command.Parameters.AddWithValue("@Tabla", tabla);

            await using var reader = await command.ExecuteReaderAsync();
            var results = new List<Dictionary<string, object>>();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.GetValue(i) is DBNull ? null : reader.GetValue(i);
                }
                results.Add(row);
            }

            _memoryCache.Set(cacheKey, results, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

            await _authUtils.InsertUserHistory(
                userId,
                "recursos_consulta_imagenes",
                $"Consultó imágenes para {tabla} ID {id_ref}"
            );

            return Ok(results);
        }

        [Authorize]
        [HttpPost("imagenes/upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> SubirImagen([FromForm] UploadRecursoRequest request)
        {
            if (request?.File == null || request.File.Length == 0)
                return BadRequest("Archivo no válido");

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            try
            {
                // Validar tabla permitida
                if (!new[] { "tareas", "proyectos", "clientes" }.Contains(request.Tabla.ToLower()))
                    return BadRequest("Tabla de referencia no válida");

                // Guardar archivo
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "imagenes");
                Directory.CreateDirectory(uploadsPath);

                var fileExtension = Path.GetExtension(request.File.FileName).ToLower();
                if (!new[] { ".jpg", ".jpeg", ".png", ".gif" }.Contains(fileExtension))
                    return BadRequest("Formato de imagen no válido");

                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsPath, uniqueFileName);
                var publicUrl = $"/uploads/imagenes/{uniqueFileName}";

                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await request.File.CopyToAsync(stream);
                }

                // Insertar en base de datos
                await using var connection = await OpenConnectionAsync();
                var query = @"
                    INSERT INTO imagenes (id_ref, tabla, url, descripcion)
                    OUTPUT INSERTED.ID
                    VALUES (@IdRef, @Tabla, @Url, @Descripcion)";

                await using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@IdRef", request.IdRef);
                command.Parameters.AddWithValue("@Tabla", request.Tabla);
                command.Parameters.AddWithValue("@Url", publicUrl);
                command.Parameters.AddWithValue("@Descripcion", request.Descripcion ?? string.Empty);
                command.Parameters.AddWithValue("@UsuarioId", userId);

                var insertedId = await command.ExecuteScalarAsync();

                // Invalidar caché
                _memoryCache.Remove($"imagenes_{request.Tabla}_{request.IdRef}");

                await _authUtils.InsertUserHistory(
                    userId,
                    "recursos_subio_imagen",
                    $"Subió imagen para {request.Tabla} ID {request.IdRef}"
                );

                return Ok(new
                {
                    Id = insertedId,
                    Url = publicUrl,
                    Message = "Imagen subida correctamente"
                });
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error al subir imagen");
            }
        }

        [Authorize]
        [HttpDelete("imagenes/delete/{id}")]
        public async Task<IActionResult> EliminarImagen(long id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            try
            {
                await using var connection = await OpenConnectionAsync();

                // Primero obtener información de la imagen
                var getQuery = @"SELECT id_ref, tabla, url FROM imagenes WHERE id = @Id";
                await using var getCommand = new SqlCommand(getQuery, connection);
                getCommand.Parameters.AddWithValue("@Id", id);

                await using var reader = await getCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return NotFound(new { Message = "Imagen no encontrada" });

                var idRef = reader.GetInt64(0);
                var tabla = reader.GetString(1);
                var url = reader.GetString(2);
                reader.Close();

                // Eliminar de la base de datos
                var deleteQuery = @"DELETE FROM imagenes WHERE id = @Id";
                await using var deleteCommand = new SqlCommand(deleteQuery, connection);
                deleteCommand.Parameters.AddWithValue("@Id", id);

                var result = await deleteCommand.ExecuteNonQueryAsync();

                if (result > 0)
                {
                    // Eliminar archivo físico
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), url.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }

                    // Invalidar caché
                    _memoryCache.Remove($"imagenes_{tabla}_{idRef}");

                    await _authUtils.InsertUserHistory(
                        userId,
                        "recursos_elimino_imagen",
                        $"Eliminó imagen ID {id}"
                    );

                    return Ok(new { Message = "Imagen eliminada correctamente" });
                }

                return NotFound(new { Message = "Imagen no encontrada" });
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error al eliminar imagen");
            }
        }

        #endregion

        #region Endpoints para Archivos

        [Authorize]
        [HttpGet("archivos/{tabla}/{id_ref}")]
        public async Task<IActionResult> ConsultarArchivos(string tabla, long id_ref)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string cacheKey = $"archivos_{tabla}_{id_ref}";

            if (_memoryCache.TryGetValue(cacheKey, out List<Dictionary<string, object>> cachedResults))
            {
                return Ok(cachedResults);
            }

            string query = @"SELECT id, id_ref, tabla, url, descripcion
                            FROM archivos 
                            WHERE id_ref = @IdRef AND tabla = @Tabla";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@IdRef", id_ref);
            command.Parameters.AddWithValue("@Tabla", tabla);

            await using var reader = await command.ExecuteReaderAsync();
            var results = new List<Dictionary<string, object>>();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.GetValue(i) is DBNull ? null : reader.GetValue(i);
                }
                results.Add(row);
            }

            _memoryCache.Set(cacheKey, results, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

            await _authUtils.InsertUserHistory(
                userId,
                "recursos_consulta_archivos",
                $"Consultó archivos para {tabla} ID {id_ref}"
            );

            return Ok(results);
        }

        [Authorize]
        [HttpPost("archivos/upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> SubirArchivo([FromForm] UploadRecursoRequest request)
        {
            if (request?.File == null || request.File.Length == 0)
                return BadRequest("Archivo no válido");

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            try
            {
                // Validar tabla permitida
                if (!new[] { "tareas", "proyectos", "clientes" }.Contains(request.Tabla.ToLower()))
                    return BadRequest("Tabla de referencia no válida");

                // Guardar archivo
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "archivos");
                Directory.CreateDirectory(uploadsPath);

                var fileExtension = Path.GetExtension(request.File.FileName);
                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsPath, uniqueFileName);
                var publicUrl = $"/uploads/archivos/{uniqueFileName}";

                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await request.File.CopyToAsync(stream);
                }

                // Insertar en base de datos
                await using var connection = await OpenConnectionAsync();
                var query = @"
                    INSERT INTO archivos 
                        (id_ref, tabla, url, descripcion)
                    OUTPUT INSERTED.ID
                    VALUES 
                        (@IdRef, @Tabla, @Url, @Descripcion)";

                await using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@IdRef", request.IdRef);
                command.Parameters.AddWithValue("@Tabla", request.Tabla);
                command.Parameters.AddWithValue("@Url", publicUrl);
                command.Parameters.AddWithValue("@Descripcion", request.Descripcion ?? string.Empty);

                var insertedId = await command.ExecuteScalarAsync();

                // Invalidar caché
                _memoryCache.Remove($"archivos_{request.Tabla}_{request.IdRef}");

                await _authUtils.InsertUserHistory(
                    userId,
                    "recursos_subio_archivo",
                    $"Subió archivo para {request.Tabla} ID {request.IdRef}"
                );

                return Ok(new
                {
                    Id = insertedId,
                    Url = publicUrl,
                    Message = "Archivo subido correctamente"
                });
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error al subir archivo");
            }
        }

        [Authorize]
        [HttpDelete("archivos/delete/{id}")]
        public async Task<IActionResult> EliminarArchivo(long id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            try
            {
                await using var connection = await OpenConnectionAsync();

                // Primero obtener información del archivo
                var getQuery = @"SELECT id_ref, tabla, url FROM archivos WHERE id = @Id";
                await using var getCommand = new SqlCommand(getQuery, connection);
                getCommand.Parameters.AddWithValue("@Id", id);

                await using var reader = await getCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return NotFound(new { Message = "Archivo no encontrado" });

                var idRef = reader.GetInt64(0);
                var tabla = reader.GetString(1);
                var url = reader.GetString(2);
                reader.Close();

                // Eliminar de la base de datos
                var deleteQuery = @"DELETE FROM archivos WHERE id = @Id";
                await using var deleteCommand = new SqlCommand(deleteQuery, connection);
                deleteCommand.Parameters.AddWithValue("@Id", id);

                var result = await deleteCommand.ExecuteNonQueryAsync();

                if (result > 0)
                {
                    // Eliminar archivo físico
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), url.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }

                    // Invalidar caché
                    _memoryCache.Remove($"archivos_{tabla}_{idRef}");

                    await _authUtils.InsertUserHistory(
                        userId,
                        "recursos_elimino_archivo",
                        $"Eliminó archivo ID {id}"
                    );

                    return Ok(new { Message = "Archivo eliminado correctamente" });
                }

                return NotFound(new { Message = "Archivo no encontrado" });
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error al eliminar archivo");
            }
        }

        #endregion

        #region Modelos

        public class UploadRecursoRequest
        {
            public long IdRef { get; set; }
            public string Tabla { get; set; }
            public string Descripcion { get; set; }
            public IFormFile File { get; set; }
        }

        #endregion
    }
}