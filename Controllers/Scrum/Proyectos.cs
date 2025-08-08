using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;

namespace MyApiProject.Controllers
{
    public partial class ProyectosController : BaseController
    {
        private readonly AuthUtils _authUtils;
        public ProyectosController(IConfiguration configuration, AuthUtils authUtils) : base(configuration, null!)
        {
            _authUtils = authUtils;
        }


        [Authorize]
        [HttpPost("api/v1/proyectos/register")]
        public async Task<IActionResult> RegistrarProyectos([FromBody] Proyectos nuevoProyectos)
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del proyectos." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum upload task", "Registro de proyectos -> " + nuevoProyectos.nombre);
            return await InsertJsonToDatabaseAsync(
                data: nuevoProyectos,
                tableName: "proyectos"
            );
        }

        [Authorize]
        [HttpPut("api/v1/proyectos/update/{id}")]
        public async Task<IActionResult> ActualizarProyectos(int id, [FromBody] ProyectosUpdate proyectosActualizado)
        {
            if (proyectosActualizado == null)
                return BadRequest(new { Message = "Datos de proyectos inválidos." });

            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del proyectos." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum update task", "Actualización de proyectos con ID " + id);

            return await UpdateJsonInDatabaseAsync(
                data: proyectosActualizado,
                tableName: "proyectos",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM proyectos WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Proyectos no encontrado." }) : null;
                }
            );
        }

        [Authorize]
        [HttpDelete("api/v1/proyectos/delete/{id}")]
        public async Task<IActionResult> EliminarProyectos(int id)
        {
            string query = @"DELETE FROM proyectos WHERE Id = @Id";

            try
            {
                await using var connection = await OpenConnectionAsync();
                await using var command = new SqlCommand(query, connection);

                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();

                if (result > 0)
                    return Ok(new { Message = "Proyectos eliminado exitosamente" });
                else
                    return NotFound(new { Message = "Proyectos no encontrado" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
