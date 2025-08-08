using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;

namespace MyApiProject.Controllers
{
    public partial class ComentariosController : BaseController
    {
        private readonly AuthUtils _authUtils;
        public ComentariosController(IConfiguration configuration, AuthUtils authUtils) : base(configuration, null!)
        {
            _authUtils = authUtils;
        }


        [Authorize]
        [HttpPost("api/v1/comentarios/register")]
        public async Task<IActionResult> RegistrarComentarios([FromBody] Comentarios nuevoComentarios)
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del comentarios." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum upload task", "Registro de comentarios -> " + nuevoComentarios.contenido);
            return await InsertJsonToDatabaseAsync(
                data: nuevoComentarios,
                tableName: "comentarios"
            );
        }

        [Authorize]
        [HttpPut("api/v1/comentarios/update/{id}")]
        public async Task<IActionResult> ActualizarComentarios(int id, [FromBody] ComentariosUpdate comentariosActualizado)
        {
            if (comentariosActualizado == null)
                return BadRequest(new { Message = "Datos de comentarios inválidos." });

            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del comentarios." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "scrum update comment", "Actualización de comentarios con ID " + id);

            return await UpdateJsonInDatabaseAsync(
                data: comentariosActualizado,
                tableName: "comentarios",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM comentarios WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Comentarios no encontrado." }) : null;
                }
            );
        }

        [Authorize]
        [HttpDelete("api/v1/comentarios/delete/{id}")]
        public async Task<IActionResult> EliminarComentarios(int id)
        {
            string query = @"DELETE FROM comentarios WHERE Id = @Id";

            try
            {
                await using var connection = await OpenConnectionAsync();
                await using var command = new SqlCommand(query, connection);

                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();

                if (result > 0)
                    return Ok(new { Message = "Comentarios eliminado exitosamente" });
                else
                    return NotFound(new { Message = "Comentarios no encontrado" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

    }
}
