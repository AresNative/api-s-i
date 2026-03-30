using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyApiProject.Hubs;
using System.Net.Http.Headers;

namespace MyApiProject.Controllers.whatsapp
{
    [ApiExplorerSettings(GroupName = "whatsapp")]
    [Route("api/v1/whatsapp")]
    [ApiController]
    public class WhatsappController : ControllerBase
    {
        private readonly IMemoryCache _memoryCache;
        private readonly IHubContext<GeneralHubs> _hubContext;
        private readonly IConfiguration _configuration;

        public WhatsappController(
            IConfiguration configuration,
            IMemoryCache memoryCache,
            IHubContext<GeneralHubs> hubContext)
        {
            _configuration = configuration;
            _memoryCache = memoryCache;
            _hubContext = hubContext;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage()
        {
            try
            {
                // PRUEBA 1: Primero intenta con HTTP directo (siempre funciona)
                var result = await SendWhatsAppWithHttpClient();
                return result;
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Failed to send WhatsApp message",
                    details = ex.Message
                });
            }
        }

        private async Task<IActionResult> SendWhatsAppWithHttpClient()
        {
            try
            {
                var accountSid = _configuration["Twilio:AccountSid"];
                var authToken = _configuration["Twilio:AuthToken"];

                // Configuración de números
                var fromNumber = _configuration["Twilio:FromNumber"] ?? "whatsapp:+15017122661";
                var toNumber = _configuration["Twilio:TestNumber"] ?? "whatsapp:+15558675310";

                if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(authToken))
                {
                    return BadRequest(new
                    {
                        error = "Twilio credentials not configured",
                        message = "Set Twilio:AccountSid and Twilio:AuthToken in appsettings.json"
                    });
                }

                // URL de la API de Twilio
                var url = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";

                using var httpClient = new HttpClient();

                // Autenticación Basic
                var authHeader = Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", authHeader);

                // Datos del mensaje
                var formData = new Dictionary<string, string>
                {
                    ["Body"] = "McAvoy or Stewart? These timelines can get so confusing.",
                    ["From"] = fromNumber,
                    ["To"] = toNumber
                };


                var response = await httpClient.PostAsync(url, new FormUrlEncodedContent(formData));
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {

                    // Notificar via SignalR
                    await _hubContext.Clients.All.SendAsync("ReceiveWhatsAppMessage", new
                    {
                        success = true,
                        method = "HTTP",
                        timestamp = DateTime.UtcNow,
                        response = responseContent
                    });

                    return Ok(new
                    {
                        success = true,
                        method = "HTTP",
                        message = "WhatsApp message sent successfully",
                        response = responseContent
                    });
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new
                    {
                        error = "Failed to send WhatsApp message",
                        statusCode = (int)response.StatusCode,
                        details = responseContent
                    });
                }
            }
            catch (HttpRequestException httpEx)
            {
                return StatusCode(503, new
                {
                    error = "Network error connecting to Twilio API",
                    details = httpEx.Message
                });
            }
        }

        // Método alternativo usando el SDK de Twilio (si se resuelve el problema)
        [HttpPost("send-sdk")]
        public async Task<IActionResult> SendMessageWithSdk()
        {
            try
            {
                var accountSid = _configuration["Twilio:AccountSid"];
                var authToken = _configuration["Twilio:AuthToken"];
                var fromNumber = _configuration["Twilio:FromNumber"] ?? "whatsapp:+15017122661";
                var toNumber = _configuration["Twilio:TestNumber"] ?? "whatsapp:+15558675310";

                if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(authToken))
                {
                    return BadRequest("Twilio credentials not configured");
                }

                // Intenta cargar los tipos dinámicamente
                var twilioClientType = Type.GetType("Twilio.TwilioClient, Twilio");
                var messageResourceType = Type.GetType("Twilio.Rest.Api.V2010.Account.MessageResource, Twilio");
                var phoneNumberType = Type.GetType("Twilio.Types.PhoneNumber, Twilio");

                if (twilioClientType == null || messageResourceType == null || phoneNumberType == null)
                {
                    return StatusCode(503, new
                    {
                        error = "Twilio SDK types not found",
                        message = "The Twilio SDK is installed but types cannot be loaded. Try the HTTP method instead."
                    });
                }

                // Inicializar TwilioClient dinámicamente
                var initMethod = twilioClientType.GetMethod("Init", new[] { typeof(string), typeof(string) });
                initMethod?.Invoke(null, new object[] { accountSid, authToken });

                // Crear PhoneNumber dinámicamente
                var fromPhoneNumber = Activator.CreateInstance(phoneNumberType, fromNumber);
                var toPhoneNumber = Activator.CreateInstance(phoneNumberType, toNumber);

                // Crear mensaje dinámicamente
                var createAsyncMethod = messageResourceType.GetMethod("CreateAsync",
                    new[] {
                        typeof(string), // body
                        phoneNumberType, // from
                        phoneNumberType  // to
                    });

                dynamic task = createAsyncMethod?.Invoke(null, new object[]
                {
                    "McAvoy or Stewart? These timelines can get so confusing.",
                    fromPhoneNumber,
                    toPhoneNumber
                });

                if (task == null)
                {
                    return StatusCode(500, "Failed to invoke Twilio SDK method");
                }

                await task;
                dynamic message = task.Result;

                return Ok(new
                {
                    success = true,
                    method = "SDK",
                    messageSid = message.Sid,
                    status = message.Status,
                    body = message.Body
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Failed to use Twilio SDK",
                    details = ex.Message,
                    recommendation = "Use the /send endpoint instead"
                });
            }
        }

        // Endpoint para verificar estado
        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            var accountSid = _configuration["Twilio:AccountSid"];
            var authToken = _configuration["Twilio:AuthToken"];

            var hasCredentials = !string.IsNullOrEmpty(accountSid) && !string.IsNullOrEmpty(authToken);

            // Verificar si los tipos de Twilio están disponibles
            var twilioTypesAvailable = Type.GetType("Twilio.TwilioClient, Twilio") != null;

            return Ok(new
            {
                status = "OK",
                timestamp = DateTime.UtcNow,
                twilio = new
                {
                    credentialsConfigured = hasCredentials,
                    accountSidPresent = !string.IsNullOrEmpty(accountSid),
                    authTokenPresent = !string.IsNullOrEmpty(authToken),
                    sdkTypesAvailable = twilioTypesAvailable
                }
            });
        }
    }
}