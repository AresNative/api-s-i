using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MyApiProject.Models;

public class TokensUtils
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokensUtils> _logger;

    public TokensUtils(IConfiguration configuration, ILogger<TokensUtils> logger)
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

    public string GenerateJwtToken(LoginModel login, int? userId = null)
    {
        if (string.IsNullOrEmpty(_configuration["JwtSettings:Key"]))
        {
            _logger.LogError("La clave JWT (JwtSettings:Key) no está configurada correctamente.");
            throw new InvalidOperationException("JWT Key no configurada.");
        }

        var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, login.Email),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Name, login.Email)
    };

        if (userId.HasValue)
        {
            claims.Add(new Claim("userId", userId.Value.ToString()));
        }

        var keyBytes = Encoding.UTF8.GetBytes(_configuration["JwtSettings:Key"]);
        var securityKey = new SymmetricSecurityKey(keyBytes);

        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["JwtSettings:Issuer"],
            audience: _configuration["JwtSettings:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: creds
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        _logger.LogInformation("Token JWT generado correctamente para el usuario: {Email}", login.Email);
        return tokenString;
    }


    public async Task<bool> IsTokenActive(string email, string token)
    {
        const string query = @"
            SELECT COUNT(*) FROM tokens_jwt 
            WHERE token = @Token AND usuario_id = (
                SELECT id FROM usuarios WHERE email = @Email
            )";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Token", token);
            command.Parameters.AddWithValue("@Email", email);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar el token JWT.");
            return false;
        }
    }

    public async Task RegistrarActividadUsuarioAsync(string email, string tipoActividad, string descripcion)
    {
        const string query = @"
            INSERT INTO actividad_usuarios (usuario_id, tipo_actividad, descripcion, fecha)
            VALUES (
                (SELECT id FROM usuarios WHERE email = @Email),
                @Tipo,
                @Descripcion,
                GETDATE()
            )";

        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);
            command.Parameters.AddWithValue("@Tipo", tipoActividad);
            command.Parameters.AddWithValue("@Descripcion", descripcion);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al registrar actividad del usuario.");
        }
    }
}
