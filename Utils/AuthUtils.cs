using Microsoft.Data.SqlClient;
using MyApiProject.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

public class AuthUtils
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthUtils> _logger;

    public AuthUtils(IConfiguration configuration, ILogger<AuthUtils> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        return connection;
    }

    public async Task<bool> IsValidUser(LoginModel login)
    {
        const string query = "SELECT COUNT(1) FROM usuarios WHERE email = @Email AND password = @Password";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", login.Email);
            command.Parameters.AddWithValue("@Password", login.Password); // ⚠️ En producción, usar hash seguro

            var result = (int)await command.ExecuteScalarAsync();
            return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar el usuario.");
            return false;
        }
    }

    public async Task<int> GetUserIdByEmail(string email)
    {
        const string query = "SELECT id FROM usuarios WHERE email = @Email";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);

            var result = await command.ExecuteScalarAsync();

            if (result != null && int.TryParse(result.ToString(), out int userId))
                return userId;

            throw new Exception("Usuario no encontrado.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener el ID del usuario.");
            throw;
        }
    }
    public async Task<Usuario?> GetUsuarioByEmailAsync(string email)
    {
        const string query = "SELECT * FROM usuarios WHERE email = @Email";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var usuario = new Usuario
                {
                    email = reader["email"]?.ToString(),
                    rol = reader["rol"]?.ToString(),
                    // Agrega aquí las demás propiedades de tu clase Usuario...
                };

                return usuario;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener el usuario por email.");
            throw;
        }
    }


    public string GetEmailFromToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);

        var claim = jwtToken.Claims.FirstOrDefault(c =>
            c.Type == "sub" || c.Type == ClaimTypes.Name || c.Type.EndsWith("/name"));

        return claim?.Value ?? throw new Exception("El token no contiene un claim de correo.");
    }

    public async Task InsertUserHistory(dynamic userId, string actividad, string movement)
    {
        const string query = "INSERT INTO actividad_usuarios (usuario_id, tipo_actividad, descripcion, fecha) VALUES (@Id, @Actividad, @Mov, GETDATE())";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", userId);
            command.Parameters.AddWithValue("@Actividad", actividad);
            command.Parameters.AddWithValue("@Mov", movement);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al registrar historial del usuario.");
        }
    }

    public async Task InsertUserSession(int userId, string token, bool isActive)
    {
        const string query = "INSERT INTO sesiones (usuario_id, token, activa) VALUES (@Id, @Token, @Activa)";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", userId);
            command.Parameters.AddWithValue("@Token", token);
            command.Parameters.AddWithValue("@Activa", isActive);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al insertar sesión de usuario.");
        }
    }

    public async Task Logout(int userId, string token)
    {
        const string query = "UPDATE sesiones SET activa = 0 WHERE usuario_id = @Id AND token = @Token";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", userId);
            command.Parameters.AddWithValue("@Token", token);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cerrar sesión.");
        }
    }

    public async Task<bool> InvalidateAllUserSessions(int userId)
    {
        const string query = "UPDATE sesiones SET activa = 0 WHERE usuario_id = @Id AND activa = 1";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", userId);

            var affected = await command.ExecuteNonQueryAsync();

            if (affected > 0)
            {
                await InsertUserHistory(userId, "Auth", "Sesiones activas cerradas");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al invalidar sesiones del usuario.");
            return false;
        }
    }

    // Guarda el token JWT emitido en la tabla tokens_jwt
    public async Task InsertJwtToken(int userId, string token, DateTime expiration)
    {
        const string query = @"
        INSERT INTO tokens_jwt (usuario_id, token, fecha_creacion, fecha_expiracion)
        VALUES (@UserId, @Token, SYSDATETIME(), @Expiration)";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Token", token);
            command.Parameters.AddWithValue("@Expiration", expiration);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al insertar token JWT en la base de datos.");
        }
    }

    // Verifica si el token está registrado y no ha expirado
    public async Task<bool> IsJwtTokenValid(string token)
    {
        const string query = @"
        SELECT COUNT(*) 
        FROM tokens_jwt 
        WHERE token = @Token AND fecha_expiracion > SYSDATETIME()";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Token", token);

            var result = (int)await command.ExecuteScalarAsync();
            return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar el token JWT.");
            return false;
        }
    }

    // Elimina (revoca) un token específico
    public async Task<bool> RevokeJwtToken(string token)
    {
        const string query = @"DELETE FROM tokens_jwt WHERE token = @Token";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Token", token);

            int rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al revocar el token JWT.");
            return false;
        }
    }

    // Elimina todos los tokens JWT del usuario
    public async Task<int> RevokeAllJwtTokens(int userId)
    {
        const string query = @"DELETE FROM tokens_jwt WHERE usuario_id = @UserId";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);

            return await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al revocar todos los tokens JWT.");
            return 0;
        }
    }

    // Extrae el token de Authorization header (uso desde controlador)
    public static string? GetTokenFromAuthorizationHeader(HttpRequest request)
    {
        var authHeader = request.Headers["Authorization"].ToString();

        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            return null;

        var parts = authHeader.Split(' ');
        if (parts.Length != 2)
            return null;

        return parts[1];
    }

}
