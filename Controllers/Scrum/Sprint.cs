using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;

namespace MyApiProject.Controllers
{
    public partial class SprintsController : BaseController
    {
        private readonly AuthUtils _authUtils;
        private readonly ScrumUtils _scrumUtils;
        public SprintsController(IConfiguration configuration, AuthUtils authUtils, ScrumUtils scrumUtils) : base(configuration, null!)
        {
            _authUtils = authUtils;
            _scrumUtils = scrumUtils;
        }


        [Authorize]
        [HttpPost("api/v1/sprints/register")]
        public async Task<IActionResult> RegistrarSprints([FromBody] Sprints nuevoSprints)
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del sprints." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum upload task", "Registro de sprints -> " + nuevoSprints.nombre);

            return await InsertJsonToDatabaseAsync(
                data: nuevoSprints,
                tableName: "sprints"
            );
        }

        [Authorize]
        [HttpPut("api/v1/sprints/update/{id}")]
        public async Task<IActionResult> ActualizarSprints(int id, [FromBody] SprintsUpdate sprintsActualizado)
        {
            if (sprintsActualizado == null)
                return BadRequest(new { Message = "Datos de sprints inválidos." });

            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del sprints." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum update task", "Actualización de sprints con ID " + id);

            return await UpdateJsonInDatabaseAsync(
                data: sprintsActualizado,
                tableName: "sprints",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM sprints WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Sprints no encontrado." }) : null;
                }
            );
        }

        [Authorize]
        [HttpDelete("api/v1/sprints/delete/{id}")]
        public async Task<IActionResult> EliminarSprints(int id)
        {
            string query = @"DELETE FROM sprints WHERE Id = @Id";

            try
            {
                await using var connection = await OpenConnectionAsync();
                await using var command = new SqlCommand(query, connection);

                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();

                if (result > 0)
                    return Ok(new { Message = "Sprints eliminado exitosamente" });
                else
                    return NotFound(new { Message = "Sprints no encontrado" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
