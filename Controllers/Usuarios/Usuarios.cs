using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using Microsoft.AspNetCore.Authorization;

namespace MyApiProject.Controllers.users
{

    [ApiExplorerSettings(GroupName = "users")]
    public partial class UsuariosController : BaseController
    {
        private readonly AuthUtils _authUtils;
        public UsuariosController(IConfiguration configuration, AuthUtils authUtils) : base(configuration, null!)
        {
            _authUtils = authUtils;
        }


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
            if (usuarioActualizado == null)
                return BadRequest(new { Message = "Datos de usuario inválidos." });

            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del usuario." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "update", "Actualización de usuario con ID " + id);

            return await UpdateJsonInDatabaseAsync(
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

        [Authorize]
        [HttpPost("api/v1/users/empleado-data")]
        public async Task<IActionResult> InsertarEmpleado([FromBody] Empleados empleado)
        {
            if (empleado == null)
                return BadRequest(new { Message = "Datos de empleado inválidos." });

            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del usuario." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "insert", "Inserción de empleado con email " + empleado.email);

            return await InsertJsonToDatabaseAsync(
                data: empleado,
                tableName: "empleados"
            );
        }

        [Authorize]
        [HttpPut("api/v1/users/update-empleado/{id}")]
        public async Task<IActionResult> ActualizarEmpleado(int id, [FromBody] EmpleadoUpdate empleadoActualizado)
        {
            if (empleadoActualizado == null)
                return BadRequest(new { Message = "Datos de empleado inválidos." });

            int userId = GetUserIdFromToken();
            if (userId == 0)
            {
                return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del empleado." });
            }
            // Ejemplo de uso seguro del AuthUtils
            await _authUtils.InsertUserHistory(userId, "update", "Actualización de empleado con ID " + id);

            return await UpdateJsonInDatabaseAsync(
                data: empleadoActualizado,
                tableName: "empleados",
                keyColumn: "id",
                keyValue: id,
                preValidation: async (connection) =>
                {
                    const string checkQuery = "SELECT COUNT(1) FROM empleados WHERE id = @Id";
                    await using var cmd = new SqlCommand(checkQuery, connection);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var exists = (int)await cmd.ExecuteScalarAsync();
                    return exists == 0 ? NotFound(new { Message = "Empleado no encontrado." }) : null;
                }
            );
        }

        [HttpDelete("api/v1/empleado/delete/{id}")]
        public async Task<IActionResult> EliminarEmpleado(int id)
        {
            string query = @"DELETE FROM empleados WHERE Id = @Id";

            try
            {
                await using var connection = await OpenConnectionAsync();
                await using var command = new SqlCommand(query, connection);

                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();

                if (result > 0)
                    return Ok(new { Message = "Empleado eliminado exitosamente" });
                else
                    return NotFound(new { Message = "Empleado no encontrado" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }


    }
}
