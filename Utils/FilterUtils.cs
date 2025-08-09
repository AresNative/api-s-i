using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using MyApiProject.Models;

public class FilterUtils
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FilterUtils> _logger;

    public FilterUtils(IConfiguration configuration, ILogger<FilterUtils> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    #region Database Utilities
    protected async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        return connection;
    }

    private List<SqlParameter> CloneParameters(List<SqlParameter> parameters)
    {
        return parameters.Select(p => new SqlParameter
        {
            ParameterName = p.ParameterName,
            Value = p.Value,
            SqlDbType = p.SqlDbType,
            Size = p.Size,
            Direction = p.Direction
        }).ToList();
    }
    #endregion

    #region Cache Utilities
    public string BuildCacheKey<T>(string prefix, bool sum, bool distinct, int page, int pageSize, T request)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        var requestJson = JsonSerializer.Serialize(request, options);
        var rawKey = $"{prefix}_{sum}_{distinct}_{page}_{pageSize}_{requestJson}";

        using (var sha1 = SHA1.Create())
        {
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(rawKey));
            var sb = new StringBuilder(40);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
    #endregion

    #region Query Building Utilities
    public (List<string> whereClauses, List<SqlParameter> parameters) BuildOptimizedFilters(
        List<BusquedaParams> filtros,
        string[] indexedColumns)
    {
        var whereClauses = new List<string>();
        var parameters = new List<SqlParameter>();
        var parameterCounters = new Dictionary<string, int>();

        // Procesamiento especial para rangos de fecha
        var fechaEmisionParams = filtros?.Where(f => f.Key == "FechaEmision").ToList() ?? new List<BusquedaParams>();
        bool fechaRangeProcessed = ProcessDateRange(fechaEmisionParams, whereClauses, parameters);

        // Procesamiento de otros filtros con prioridad para columnas indexadas
        if (filtros != null)
        {
            // Procesar primero las columnas indexadas
            foreach (var filter in filtros
                .Where(f => f != null && indexedColumns.Contains(f.Key, StringComparer.OrdinalIgnoreCase)))
            {
                if (fechaRangeProcessed && filter.Key == "FechaEmision") continue;

                var (clause, param) = BuildOptimizedFilterClause(filter, parameterCounters);
                if (clause != null)
                {
                    whereClauses.Add(clause);
                    parameters.Add(param);
                }
            }

            // Luego procesar las no indexadas
            foreach (var filter in filtros
                .Where(f => f != null && !indexedColumns.Contains(f.Key, StringComparer.OrdinalIgnoreCase)))
            {
                if (fechaRangeProcessed && filter.Key == "FechaEmision") continue;

                var (clause, param) = BuildOptimizedFilterClause(filter, parameterCounters);
                if (clause != null)
                {
                    whereClauses.Add(clause);
                    parameters.Add(param);
                }
            }
        }

        whereClauses = GroupConditions(whereClauses);
        return (whereClauses, parameters);
    }

    public (string clause, SqlParameter param) BuildOptimizedFilterClause(
        BusquedaParams filter,
        Dictionary<string, int> parameterCounters)
    {
        if (string.IsNullOrWhiteSpace(filter.Value)) return (null, null);

        string operatorClause = filter.Operator?.ToUpper() switch
        {
            "LIKE" => "LIKE",
            "=" => "=",
            ">=" => ">=",
            "<=" => "<=",
            ">" => ">",
            "<" => "<",
            "<>" => "<>",
            "IN" => "IN",
            _ => "="
        };

        var column = filter.Key;
        parameterCounters.TryGetValue(column, out int count);
        parameterCounters[column] = count + 1;

        var paramName = $"@{column}_{count}";
        string clause;

        // Manejo especial para operador IN
        if (operatorClause == "IN")
        {
            var values = filter.Value.Split(',');
            var paramNames = new List<string>();
            var parameters = new List<SqlParameter>();
            for (int i = 0; i < values.Length; i++)
            {
                var inParamName = $"{paramName}_{i}";
                paramNames.Add(inParamName);
                parameters.Add(new SqlParameter(inParamName, values[i].Trim()));
            }
            clause = $"{column} IN ({string.Join(", ", paramNames)})";
            return (clause, null);
        }
        else
        {
            clause = $"{column} {operatorClause} {paramName}";
        }

        object paramValue = operatorClause == "LIKE" ? $"%{filter.Value}%" : filter.Value;

        // Manejo especial para tipos de datos
        if (DateTime.TryParse(filter.Value, out var dateValue))
        {
            paramValue = dateValue;
        }
        else if (decimal.TryParse(filter.Value, out var decimalValue))
        {
            paramValue = decimalValue;
        }
        else if (int.TryParse(filter.Value, out var intValue))
        {
            paramValue = intValue;
        }

        return (clause, new SqlParameter(paramName, paramValue));
    }

    public bool ProcessDateRange(List<BusquedaParams> fechaParams, List<string> whereClauses, List<SqlParameter> parameters)
    {
        if (fechaParams.Count == 2)
        {
            var minFecha = fechaParams.FirstOrDefault(f => f.Operator == ">=");
            var maxFecha = fechaParams.FirstOrDefault(f => f.Operator == "<=");

            if (minFecha != null && maxFecha != null &&
                DateTime.TryParse(minFecha.Value, out var minDate) &&
                DateTime.TryParse(maxFecha.Value, out var maxDate))
            {
                whereClauses.Add("FechaEmision BETWEEN @FechaEmisionMin AND @FechaEmisionMax");
                parameters.Add(new SqlParameter("@FechaEmisionMin", minDate));
                parameters.Add(new SqlParameter("@FechaEmisionMax", maxDate));
                return true;
            }
        }
        return false;
    }

    public string GetSelectColumns(List<SumaParams> selects)
    {
        if (selects == null) return string.Empty;

        return string.Join(", ", selects
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Key))
            .Select(s => s.Key));
    }

    public List<string> GroupConditions(List<string> whereClauses)
    {
        return whereClauses
            .GroupBy(c => c.Split(' ', 2)[0])
            .Select(g => g.Count() > 1
                ? $"({string.Join(" OR ", g)})"
                : g.First())
            .ToList();
    }
    #endregion

    #region Query Execution Utilities
    public async Task<(int totalRecords, List<Dictionary<string, object>> results)> ExecuteOptimizedQueryAsync(
        string countQuery,
        string dataQuery,
        List<SqlParameter> parameters,
        int offset,
        int pageSize)
    {
        try
        {
            await using var connection = await OpenConnectionAsync();

            // Ejecutar COUNT en un comando separado
            int totalRecords = 0;
            await using (var countCommand = new SqlCommand(countQuery, connection))
            {
                var countParams = CloneParameters(parameters);
                countCommand.Parameters.AddRange(countParams.ToArray());
                countCommand.CommandTimeout = 60;
                totalRecords = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
            }

            // Ejecutar dataQuery con paginación
            var results = new List<Dictionary<string, object>>();
            await using (var dataCommand = new SqlCommand(dataQuery, connection))
            {
                var dataParams = CloneParameters(parameters);
                dataParams.Add(new SqlParameter("@Offset", offset));
                dataParams.Add(new SqlParameter("@PageSize", pageSize));
                dataCommand.Parameters.AddRange(dataParams.ToArray());
                dataCommand.CommandTimeout = 120;

                await using var reader = await dataCommand.ExecuteReaderAsync(CommandBehavior.Default);

                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i) is DBNull ? null : reader.GetValue(i);
                    }
                    results.Add(row);
                }
            }

            return (totalRecords, results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing optimized query");
            throw;
        }
    }
    #endregion
}