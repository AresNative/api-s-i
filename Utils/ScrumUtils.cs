using Microsoft.Data.SqlClient;

public class ScrumUtils
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScrumUtils> _logger;

    public ScrumUtils(IConfiguration configuration, ILogger<ScrumUtils> logger)
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

    public async Task RegistrarHistorialTareaAsync(int tareaId, string descripcionCambio)
    {
        const string query = @"
            INSERT INTO historial_tareas (tarea_id, descripcion_cambio, fecha)
            VALUES (@TareaId, @DescripcionCambio, GETDATE())";
        try
        {
            await using var conn = await OpenConnectionAsync();
            await using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@TareaId", tareaId);
            cmd.Parameters.AddWithValue("@DescripcionCambio", descripcionCambio);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al registrar historial de tarea.");
        }
    }
}
