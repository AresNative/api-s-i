using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MyApiProject.Models;
namespace MyApiProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : BaseController
    {
        private readonly AuthUtils _authUtils;
        private readonly TokensUtils _tokensUtils;
        private readonly ILogger<AuthController> _logger;
        private readonly IMemoryCache _memoryCache;

        public AuthController(IConfiguration configuration, AuthUtils authUtils, TokensUtils tokensUtils, ILogger<AuthController> logger, IMemoryCache memoryCache) : base(configuration, null!)
        {
            _authUtils = authUtils;
            _tokensUtils = tokensUtils;
            _logger = logger;
            _memoryCache = memoryCache;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel login)
        {
            if (!await _authUtils.IsValidUser(login))
            {
                _logger.LogWarning("Login fallido para el correo: {Email}", login.Email);
                return Unauthorized("Credenciales incorrectas");
            }

            var userId = await _authUtils.GetUserIdByEmail(login.Email);

            var usuario = await _authUtils.GetUsuarioByEmailAsync(login.Email);
            if (usuario == null)
            {
                _logger.LogError("El usuario con email {Email} no fue encontrado después de validar credenciales.", login.Email);
                return NotFound("Usuario no encontrado.");
            }

            var token = _tokensUtils.GenerateJwtToken(login, userId);
            var expiration = DateTime.UtcNow.AddHours(10); // ajustar si el token tiene duración distinta

            await _authUtils.InsertUserSession(userId, token, true);
            await _authUtils.InsertJwtToken(userId, token, expiration);
            await _authUtils.InsertUserHistory(userId, "Auth", "Inicio de sesión");

            _memoryCache.Set($"token_{userId}", token, TimeSpan.FromHours(24));

            _logger.LogInformation("El usuario {Email} inició sesión correctamente.", login.Email);

            return Ok(new
            {
                Token = token,
                ID = userId,
                Email = usuario.email,
                Rol = usuario.rol
            });
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var token = AuthUtils.GetTokenFromAuthorizationHeader(Request);
                if (token == null) return Unauthorized("Token inválido");

                var email = _authUtils.GetEmailFromToken(token);
                var userId = await _authUtils.GetUserIdByEmail(email);

                await _authUtils.Logout(userId, token);
                await _authUtils.RevokeJwtToken(token);
                await _authUtils.InsertUserHistory(userId, "Auth", "Cierre de sesión");

                _memoryCache.Remove($"token_{userId}");

                _logger.LogInformation("Usuario {Email} cerró sesión.", email);

                return Ok("Sesión cerrada");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante el cierre de sesión.");
                return StatusCode(500, "Error interno al cerrar sesión");
            }
        }

        [Authorize]
        [HttpGet("verify")]
        public async Task<IActionResult> VerifyToken()
        {
            try
            {
                var token = AuthUtils.GetTokenFromAuthorizationHeader(Request);
                if (token == null) return Unauthorized("Token inválido");

                var isValid = await _authUtils.IsJwtTokenValid(token);
                if (!isValid)
                {
                    _logger.LogWarning("Token JWT no válido o expirado.");
                    return Unauthorized("Token expirado o inválido");
                }

                var email = _authUtils.GetEmailFromToken(token);
                _logger.LogInformation("Token válido verificado para el usuario {Email}.", email);
                return Ok("Token válido");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar el token.");
                return StatusCode(500, "Error interno al verificar token");
            }
        }

        [Authorize]
        [HttpPost("logout-all")]
        public async Task<IActionResult> LogoutAll()
        {
            try
            {
                var token = AuthUtils.GetTokenFromAuthorizationHeader(Request);
                if (token == null) return Unauthorized("Token inválido");

                var email = _authUtils.GetEmailFromToken(token);
                int userId = GetUserIdFromToken();
                if (userId == 0)
                {
                    return Unauthorized(new { Message = "Token no válido o no se pudo extraer el ID del usuario." });
                }

                var sessionsClosed = await _authUtils.InvalidateAllUserSessions(userId);
                var tokensRevoked = await _authUtils.RevokeAllJwtTokens(userId);

                if (!sessionsClosed && tokensRevoked == 0)
                    return BadRequest("No se encontraron sesiones activas o tokens válidos.");

                await _authUtils.InsertUserHistory(userId, "Auth", "Todas las sesiones cerradas");
                _memoryCache.Remove($"token_{userId}");

                _logger.LogInformation("Usuario {Email} cerró todas sus sesiones y tokens.", email);

                return Ok("Todas las sesiones y tokens han sido cerradas.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cerrar todas las sesiones.");
                return StatusCode(500, "Error interno al cerrar sesiones");
            }
        }
    }
}
