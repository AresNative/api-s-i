using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;

namespace MyApiProject.Controllers
{
    public partial class TareasController : BaseController
    {
        private readonly AuthUtils _authUtils;
        private readonly ScrumUtils _scrumUtils;

        public TareasController(IConfiguration configuration, AuthUtils authUtils, ScrumUtils scrumUtils)
            : base(configuration, null!)
        {
            _authUtils = authUtils;
            _scrumUtils = scrumUtils;
        }

        private int ObtenerUsuarioId()
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
                throw new UnauthorizedAccessException("Token no válido o no se pudo extraer el ID del usuario.");
            return userId;
        }

        private static int? ExtraerIdDeResultado(IActionResult result)
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

        [Authorize]
        [HttpGet("api/v1/tareas/consultar")]
        public async Task<IActionResult> ConsultarTarea(int sprint_id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string query = @"SELECT * FROM tareas WHERE sprint_id = @SprintId";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);

            command.Parameters.AddWithValue("@SprintId", sprint_id);

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
            await _authUtils.InsertUserHistory(
                                userId,
                                "scrum load tasks",
                                $"Consulta de tareas para el sprint ID {sprint_id}"
                            );
            return Ok(results);
        }

        [Authorize]
        [HttpPost("api/v1/tareas/register")]
        public async Task<IActionResult> RegistrarTarea([FromBody] Tarea nuevoTarea)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var insertResult = await InsertJsonToDatabaseAsync(
                data: nuevoTarea,
                tableName: "tareas"
            );

            var tareaId = ExtraerIdDeResultado(insertResult);
            if (tareaId.HasValue && tareaId.Value > 0)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "scrum upload task",
                    $"Registro de tarea -> {nuevoTarea.titulo}"
                );

                await _scrumUtils.RegistrarHistorialTareaAsync(
                    tareaId.Value,
                    $"Tarea registrada por el usuario con ID {userId}"
                );
            }

            return insertResult;
        }

        [Authorize]
        [HttpPut("api/v1/tareas/update/{id}")]
        public async Task<IActionResult> ActualizarTarea(int id, [FromBody] TareaUpdate tareaActualizado)
        {
            if (tareaActualizado == null)
                return BadRequest(new { Message = "Datos de tarea inválidos." });

            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            var updateResult = await UpdateJsonInDatabaseAsync(
                data: tareaActualizado,
                tableName: "tareas",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM tareas WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Tarea no encontrada." }) : null;
                }
            );

            if (updateResult is OkObjectResult)
            {
                await _authUtils.InsertUserHistory(
                    userId,
                    "scrum update task",
                    $"Actualización de tarea con ID {id}"
                );

                await _scrumUtils.RegistrarHistorialTareaAsync(
                    id,
                    $"Tarea actualizada por el usuario con ID {userId}"
                );
            }

            return updateResult;
        }

        [Authorize]
        [HttpDelete("api/v1/tareas/delete/{id}")]
        public async Task<IActionResult> EliminarTarea(int id)
        {
            int userId;
            try { userId = ObtenerUsuarioId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { Message = ex.Message }); }

            string query = @"UPDATE tareas SET estado = 'archivado' WHERE Id = @Id";

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
                        $"Eliminación de tarea con ID {id}"
                    );

                    await _scrumUtils.RegistrarHistorialTareaAsync(
                        id,
                        $"Tarea archivada por el usuario con ID {userId}"
                    );

                    return Ok(new { Message = "Tarea eliminada exitosamente" });
                }
                else
                {
                    return NotFound(new { Message = "Tarea no encontrada" });
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }
    }
}
