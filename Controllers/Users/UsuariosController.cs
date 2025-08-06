using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;

namespace MyApiProject.Controllers
{
    public partial class UsuariosController : BaseController
    {
        private readonly AuthUtils _authUtils;
        public UsuariosController(IConfiguration configuration) : base(configuration, null) { }

        [HttpPost("api/v1/users/register")]
        public async Task<IActionResult> RegistrarUsuario([FromBody] Usuario nuevoUsuario)
        {
            return await InsertJsonToDatabaseAsync(
                data: nuevoUsuario,
                tableName: "usuarios",
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM usuarios WHERE email = @Email";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Email", nuevoUsuario.email);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists > 0 ? Conflict(new { Message = "Ya existe un usuario con ese email." }) : null;
                }
            );
        }

        [Authorize]
        [HttpPut("api/v1/users/update/{id}")]
        public async Task<IActionResult> ActualizarUsuario(int id, [FromBody] UsuarioUpdate usuarioActualizado)
        {
            var userId = GetUserIdFromToken();
            await _authUtils.InsertUserHistory(userId, "update", "Actrualización de usuario con ID: " + id);

            var response = await UpdateJsonInDatabaseAsync(
                data: usuarioActualizado,
                tableName: "usuarios",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM usuarios WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Usuario no encontrado." }) : null;
                }
            );
            if (response is ConflictObjectResult conflictResult)
            {
                return Conflict(new { Message = "Ya existe un usuario con ese email." });
            }
            return response;
        }

        [Authorize]
        [HttpDelete("api/v1/users/delete/{id}")]
        public async Task<IActionResult> EliminarUsuario(int id)
        {
            string query = @"DELETE FROM usuarios WHERE Id = @Id";

            try
            {
                await using var connection = await OpenConnectionAsync();
                await using var command = new SqlCommand(query, connection);

                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();

                if (result > 0)
                    return Ok(new { Message = "Usuario eliminado exitosamente" });
                else
                    return NotFound(new { Message = "Usuario no encontrado" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }
        /*[HttpPost("api/v2/postulaciones")]
         public async Task<IActionResult> InsertarPostulacion([FromForm] UploadPostulacion request)
        {
            if (string.IsNullOrWhiteSpace(request.PostulacionForm))
                return BadRequest("Faltan los datos JSON.");

            var data = JsonConvert.DeserializeObject<PostulacionData>(request.PostulacionForm);

            return await InsertJsonToDatabaseAsync(
                data: data!,
                tableName: "LOCAL_TC032391E.dbo.Postulaciones",
                file: request.File,
                fileColumn: "file"
            );
        } */

    }
}
