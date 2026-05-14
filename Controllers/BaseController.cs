// Controllers/BaseController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using MyApiProject.Models;
using MyApiProject.Services;
using Newtonsoft.Json;

namespace MyApiProject.Controllers
{
    public abstract class BaseController : ControllerBase
    {
        private readonly string _connectionString;
        protected readonly IMemoryCache _cache;
        protected readonly QueryBuilder QB;

        public BaseController(IConfiguration configuration, IMemoryCache cache)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException("Cadena de conexión 'DefaultConnection' no encontrada");
            _cache = cache;
            QB = new QueryBuilder();
        }

        protected async Task<SqlConnection> OpenConnectionAsync()
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }

        protected IActionResult HandleException(Exception ex, string? query = null)
        {
            if (ex is OperationCanceledException || ex is TaskCanceledException)
                return StatusCode(499, new { Message = "Solicitud cancelada por el cliente" });

            var sanitizedMessage = ex.Message.Replace("\r", "").Replace("\n", " ");
            var sanitizedQuery = query?.Replace("\r", "").Replace("\n", " ");

            return StatusCode(500, new
            {
                Message = $"Error: {sanitizedMessage}",
                Query = sanitizedQuery
            });
        }

        protected IActionResult HandleException(Exception ex, int statusCode)
        {
            if (ex is OperationCanceledException || ex is TaskCanceledException)
                return StatusCode(499, "Solicitud cancelada por el cliente");

            return StatusCode(statusCode, new { Message = $"Error: {ex.Message}" });
        }
        protected int GetUserIdFromToken()
        {
            var userIdClaim = User?.Claims?.FirstOrDefault(c => c.Type == "userId");

            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }

            return 0; // Valor por defecto si no se encuentra el claim
        }
        protected int ObtenerUsuarioId()
        {
            int userId = GetUserIdFromToken();
            if (userId == 0)
                throw new UnauthorizedAccessException("Token no válido o no se pudo extraer el ID del usuario.");
            return userId;
        }

        protected static int? ExtraerIdDeResultado(IActionResult result)
        {
            if (result is OkObjectResult ok && ok.Value is not null)
            {
                var resultData = JsonConvert.DeserializeObject<dynamic>(
                    JsonConvert.SerializeObject(ok.Value)
                );
                return (int?)resultData?.Id;
            }
            return null;
        }
        protected async Task<IActionResult> InsertJsonToDatabaseAsync<T>(
            T data,
            string tableName,
            IFormFile? file = null,
            string fileColumn = "file",
            Dictionary<string, object>? extraColumns = null,
            Func<SqlConnection, Task<IActionResult>>? preValidation = null
        )
        {
            try
            {
                // Guardar archivo si existe
                string? filePath = null;
                if (file != null)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
                    Directory.CreateDirectory(uploadsFolder);

                    var fileExtension = Path.GetExtension(file.FileName);
                    var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                    filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    await using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);
                }

                await using var connection = await OpenConnectionAsync();

                if (preValidation != null)
                {
                    var validationResult = await preValidation(connection);
                    if (validationResult != null) return validationResult;
                }

                // Obtener propiedades del modelo
                var properties = typeof(T).GetProperties()
                    .Where(p => p.GetValue(data) != null)
                    .ToList();

                var allColumns = new List<string>();
                var allParameters = new List<string>();
                var sqlParameters = new List<SqlParameter>();

                foreach (var prop in properties)
                {
                    allColumns.Add($"[{prop.Name}]");
                    allParameters.Add($"@{prop.Name}");
                    sqlParameters.Add(new SqlParameter($"@{prop.Name}", prop.GetValue(data) ?? DBNull.Value));
                }

                // Extra columns (manuales)
                if (extraColumns != null)
                {
                    foreach (var entry in extraColumns)
                    {
                        allColumns.Add($"[{entry.Key}]");
                        allParameters.Add($"@{entry.Key}");
                        sqlParameters.Add(new SqlParameter($"@{entry.Key}", entry.Value ?? DBNull.Value));
                    }
                }

                // Archivo (si se incluye)
                if (filePath != null)
                {
                    allColumns.Add($"[{fileColumn}]");
                    allParameters.Add("@FilePath");
                    sqlParameters.Add(new SqlParameter("@FilePath", filePath));
                }

                var query = $@"
                            INSERT INTO [{tableName}] ({string.Join(", ", allColumns)})
                            OUTPUT INSERTED.ID
                            VALUES ({string.Join(", ", allParameters)});
                            ";

                await using var command = new SqlCommand(query, connection);
                command.Parameters.AddRange(sqlParameters.ToArray());

                var insertedId = await command.ExecuteScalarAsync();

                return Ok(new { Message = $"{tableName} insertado correctamente.", Id = insertedId });
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Error al insertar en {tableName}.");
            }
        }
        protected async Task<IActionResult> UpdateJsonInDatabaseAsync<T>(
            T data,
            string tableName,
            string keyColumn,
            object keyValue,
            Func<SqlConnection, Task<IActionResult?>>? preValidation = null
        )
        {
            try
            {
                await using var connection = await OpenConnectionAsync();

                if (preValidation != null)
                {
                    var validationResult = await preValidation(connection);
                    if (validationResult != null) return validationResult;
                }

                // Obtener propiedades con valores no nulos, excluyendo la clave primaria
                var properties = typeof(T).GetProperties()
                    .Where(p => p.Name.ToLower() != keyColumn.ToLower())
                    .Where(p => p.GetValue(data) != null)
                    .ToList();

                if (!properties.Any())
                {
                    return BadRequest(new { Message = "No se proporcionaron campos para actualizar." });
                }

                var setClause = string.Join(", ", properties.Select(p => $"{p.Name} = @{p.Name}"));
                var query = $"UPDATE {tableName} SET {setClause} WHERE {keyColumn} = @KeyValue";

                await using var command = new SqlCommand(query, connection);

                foreach (var prop in properties)
                {
                    var value = prop.GetValue(data);
                    command.Parameters.AddWithValue($"@{prop.Name}", value);
                }

                command.Parameters.AddWithValue("@KeyValue", keyValue);

                var result = await command.ExecuteNonQueryAsync();

                return result > 0
                    ? Ok(new { Message = "Información actualizada correctamente." })
                    : NotFound(new { Message = "Información no encontrada." });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }
        // Métodos agregados
        protected void BuildFilters(FiltrosRequest request, List<string> whereClauses, List<SqlParameter> parameters,
            Dictionary<string, int> parameterCounters)
        {
            // Filtrar elementos vacíos primero
            var validFiltros = request.Filtros
                .Where(f => !string.IsNullOrWhiteSpace(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
                .ToList();

            var fechaParams = validFiltros.Where(f => f.Key == "Fecha").ToList();
            bool fechaRangeProcessed = false;

            if (fechaParams.Count == 2)
            {
                var minFecha = fechaParams.FirstOrDefault(f => f.Operator == ">=");
                var maxFecha = fechaParams.FirstOrDefault(f => f.Operator == "<=");

                if (minFecha != null && maxFecha != null &&
                    !string.IsNullOrWhiteSpace(minFecha.Value) &&
                    !string.IsNullOrWhiteSpace(maxFecha.Value))
                {
                    whereClauses.Add($"{fechaParams.First().Key} BETWEEN @FechaMin AND @FechaMax");
                    parameters.Add(new SqlParameter("@FechaMin", DateTime.Parse(minFecha.Value)));
                    parameters.Add(new SqlParameter("@FechaMax", DateTime.Parse(maxFecha.Value)));
                    fechaRangeProcessed = true;
                }
            }

            foreach (var filter in validFiltros)
            {
                if (fechaRangeProcessed && filter.Key == "Fecha") continue;

                string operatorClause = filter.Operator?.ToLower() switch
                {
                    "like" => "LIKE",
                    "=" => "=",
                    ">=" => ">=",
                    "<=" => "<=",
                    ">" => ">",
                    "<" => "<",
                    "<>" => "<>",
                    _ => "=" // Valor por defecto más seguro
                };

                var column = filter.Key;
                if (!parameterCounters.ContainsKey(column))
                    parameterCounters[column] = 0;
                else
                    parameterCounters[column]++;

                var paramName = $"@{column.Replace(".", "_")}_{parameterCounters[column]}"; // Reemplazar . por _ en parámetros
                whereClauses.Add($"{column} {operatorClause} {paramName}");

                var paramValue = operatorClause == "LIKE" ? $"%{filter.Value}%" : filter.Value;
                parameters.Add(new SqlParameter(paramName, paramValue));
            }
        }

        protected List<string> AgruparCondiciones(List<string> whereClauses)
        {
            var dict = new Dictionary<string, List<string>>();

            foreach (var clause in whereClauses)
            {
                var key = clause.Split(' ', 2)[0]; // Tomamos la primera palabra como clave
                if (!dict.ContainsKey(key))
                    dict[key] = new List<string>();
                dict[key].Add(clause);
            }

            return dict.Select(kvp =>
                kvp.Value.Count > 1
                    ? $"({string.Join(" OR ", kvp.Value)})"
                    : kvp.Value.First()
            ).ToList();
        }

        protected async Task<string> GuardarArchivo(IFormFile archivo)
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads/listas");
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(archivo.FileName)}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await archivo.CopyToAsync(stream);
            }

            return filePath;
        }

        // Métodos auxiliares para construir las cláusulas
        protected string GetGroupByColumns(FiltrosRequest request)
        {
            var groupByColumns = request.Selects?
                .Where(s => !string.IsNullOrWhiteSpace(s.Key))
                .Select(s => s.Key)
                .ToList();

            return groupByColumns != null && groupByColumns.Any()
                ? string.Join(", ", groupByColumns)
                : "id";
        }

        // Modifica el método BuildSelectClause
        protected (string selectClause, string groupByClause) BuildSelectClause(FiltrosRequest request)
        {
            var selectParts = new List<string>();
            var groupByParts = new List<string>();

            // Procesar columnas normales (SELECT)
            var validSelects = request.Selects?
                .Where(s => !string.IsNullOrWhiteSpace(s.Key))
                .ToList();

            if (validSelects != null && validSelects.Any())
            {
                foreach (var select in validSelects)
                {
                    if (!string.IsNullOrWhiteSpace(select.Alias))
                    {
                        selectParts.Add($"{select.Key} AS {select.Alias}");
                    }
                    else
                    {
                        selectParts.Add(select.Key);
                    }
                    groupByParts.Add(select.Key);   // 👈 columna original si no hay alias
                }
            }

            // Procesar operaciones de agregación
            var validAgregaciones = request.Agregaciones?
                .Where(a => !string.IsNullOrWhiteSpace(a.Key))
                .ToList();

            if (validAgregaciones != null && validAgregaciones.Any())
            {
                foreach (var agregacion in validAgregaciones)
                {
                    string operation = !string.IsNullOrWhiteSpace(agregacion.Operation)
                        ? agregacion.Operation.ToUpper()
                        : "SUM";

                    // Validar operaciones SQL permitidas
                    string sqlOperation = operation switch
                    {
                        "SUM" => "SUM",
                        "COUNT" => "COUNT",
                        "AVG" => "AVG",
                        "MIN" => "MIN",
                        "MAX" => "MAX",
                        "DISTINCT" => "DISTINCT",
                        _ => "SUM" // Valor por defecto
                    };

                    string alias = !string.IsNullOrWhiteSpace(agregacion.Alias)
                        ? agregacion.Alias
                        : $"{sqlOperation}_{agregacion.Key}";

                    if (sqlOperation == "DISTINCT")
                    {
                        selectParts.Add($"DISTINCT {agregacion.Key} AS {alias}");
                    }
                    else
                    {
                        selectParts.Add($"{sqlOperation}({agregacion.Key}) AS {alias}");
                    }
                }
            }

            // Si no hay selects ni agregaciones, devolver todas las columnas
            string selectClause = selectParts.Any() ? string.Join(", ", selectParts) : "*";
            string groupByClause = groupByParts.Any()
                ? $"GROUP BY {string.Join(", ", groupByParts)}"
                : "";

            return (selectClause, groupByClause);
        }
        protected string BuildOrderByClause(FiltrosRequest request)
        {
            // Solo procesar órdenes que tengan Key no vacío
            var validOrders = request.Order?
                .Where(o => !string.IsNullOrWhiteSpace(o.Key))
                .ToList();

            if (validOrders == null || !validOrders.Any())
            {
                // Buscar un campo seguro para ordenar por defecto
                var safeOrderField = FindSafeOrderField(request);
                return $"ORDER BY {safeOrderField}";
            }

            var orderParts = new List<string>();

            foreach (var order in validOrders)
            {
                var direction = !string.IsNullOrWhiteSpace(order.Direction) &&
                               order.Direction.ToUpper() == "DESC" ? "DESC" : "ASC";

                // Usar la clave directamente (ya debe estar calificada con tabla si es necesario)
                orderParts.Add($"{order.Key} {direction}");
            }

            return $"ORDER BY {string.Join(", ", orderParts)}";
        }
        private string ResolveOrderByExpression(string orderBy, string selectClause, string groupByClause)
        {
            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                return orderBy.StartsWith("ORDER BY ", StringComparison.OrdinalIgnoreCase)
                    ? orderBy[9..]
                    : orderBy;
            }

            // Fallback 1: primera columna del GROUP BY
            // IMPORTANTE: en el CTE _Grouped → _Page, las columnas calificadas
            // (alias.columna) se convierten en columnas sin prefijo dentro del CTE.
            // "ventad.Articulo" en el GROUP BY → columna "Articulo" en _Grouped.
            // Devolver solo la parte final del identificador para evitar error 4104.
            var fromGroup = QB.ExtractFirstGroupColumn(groupByClause);
            if (!string.IsNullOrEmpty(fromGroup))
                return StripTableAlias(fromGroup);

            // Fallback 2: primera columna/alias del SELECT
            var candidate = QB.ExtractFirstSelectColumnOrAlias(selectClause);

            // Alias de función de agregado sin GROUP BY → (SELECT NULL)
            // (el alias no está disponible en el mismo nivel que ROW_NUMBER)
            if (candidate.StartsWith("[") && string.IsNullOrEmpty(groupByClause))
                return "(SELECT NULL)";

            // Columna calificada en SELECT sin alias → quitar prefijo para el CTE
            if (!string.IsNullOrEmpty(groupByClause))
                return StripTableAlias(candidate);

            return candidate;
        }
        protected static void NormalizeRequestAliases(FiltrosRequest request, string fromClause)
        {
            // Extraer aliases: { "CB" -> "cb", "ART" -> "ART", "P" -> "P", ... }
            var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var aliasMatches = System.Text.RegularExpressions.Regex.Matches(
                fromClause,
                @"\b(\w+)\s+AS\s+(\w+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (System.Text.RegularExpressions.Match m in aliasMatches)
                aliasMap[m.Groups[2].Value] = m.Groups[2].Value; // alias real tal como está

            if (!aliasMap.Any()) return;

            static string FixKey(string key, Dictionary<string, string> map)
            {
                if (string.IsNullOrWhiteSpace(key) || !key.Contains('.')) return key;
                var dot = key.IndexOf('.');
                var alias = key[..dot];
                var rest = key[dot..]; // incluye el punto
                return map.TryGetValue(alias, out var realAlias) ? realAlias + rest : key;
            }

            // Normalizar Selects
            foreach (var s in request.Selects)
                s.Key = FixKey(s.Key, aliasMap);

            // Normalizar Agregaciones
            foreach (var a in request.Agregaciones)
                a.Key = FixKey(a.Key, aliasMap);

            // Normalizar Filtros planos
            foreach (var f in request.Filtros)
                f.Key = FixKey(f.Key, aliasMap);

            // Normalizar grupos FiltrosAnd y FiltrosOr
            foreach (var grupo in request.FiltrosAnd.Concat(request.FiltrosOr))
                foreach (var f in grupo.Filtros)
                    f.Key = FixKey(f.Key, aliasMap);

            // Normalizar Order
            foreach (var o in request.Order)
                o.Key = FixKey(o.Key, aliasMap);

            // Normalizar Having
            foreach (var h in request.Having)
                h.Key = FixKey(h.Key, aliasMap);
        }
        private static async Task<List<Dictionary<string, object?>>> ReadResultsAsync(
            SqlCommand command, int expectedPageSize)
        {
            var results = new List<Dictionary<string, object?>>(expectedPageSize);

            await using var reader = await command.ExecuteReaderAsync(
                System.Data.CommandBehavior.SequentialAccess | System.Data.CommandBehavior.SingleResult);

            var fieldNames = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                fieldNames[i] = reader.GetName(i);

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(fieldNames.Length);
                for (int i = 0; i < fieldNames.Length; i++)
                {
                    if (fieldNames[i] == "_RowNum") continue;
                    row[fieldNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                results.Add(row);
            }

            return results;
        }
        private async Task<List<Dictionary<string, object?>>> ExecuteQueryDirectAsync(
                    string query, List<SqlParameter> parameters)
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            QB.AddParametersTo(command, parameters);
            return await ReadResultsAsync(command, 1);
        }

        private async Task<(List<Dictionary<string, object?>> Data, long Total)> ExecuteTempTableStrategyAsync(
           string selectClause, string fromClause, string whereClause,
           string groupByClause, string orderByExpression,
           int offset, int pageSize, List<SqlParameter> parameters, bool hasDistinct = false, string havingClause = "")
        {
            var tempName = $"#Tmp_{Guid.NewGuid():N}";
            int rowLimit = offset + pageSize + 1000; // un poco más para el conteo

            await using var connection = await OpenConnectionAsync();

            // 1. Llenar tabla temporal
            var createQuery = QB.BuildTempTableQuery(
                selectClause, fromClause, whereClause, groupByClause, tempName, rowLimit, havingClause);

            await using var createCmd = new SqlCommand(createQuery, connection);
            QB.AddParametersTo(createCmd, parameters);
            await createCmd.ExecuteNonQueryAsync();

            // 2. Crear índice en la columna de orden (mejora dramáticamente el ROW_NUMBER)
            var indexCol = QB.ExtractFirstOrderColumn(orderByExpression);
            if (!string.IsNullOrWhiteSpace(indexCol) && !QB.IsComplexExpression(indexCol))
            {
                var indexSql = $"CREATE CLUSTERED INDEX IX_{tempName.Replace("#", "")} " +
                               $"ON {tempName} ({indexCol})";
                await using var idxCmd = new SqlCommand(indexSql, connection);
                try { await idxCmd.ExecuteNonQueryAsync(); } catch { /* columna puede no existir */ }
            }

            // 3. Contar filas en la temp
            var countCmd = new SqlCommand($"SELECT COUNT_BIG(*) FROM {tempName}", connection);
            var total = Convert.ToInt64(await countCmd.ExecuteScalarAsync());

            // 4. Paginar desde la temp
            var pageQuery = $@"
            WITH _Page AS (
                SELECT *, ROW_NUMBER() OVER (ORDER BY {orderByExpression}) AS _RowNum
                FROM {tempName}
            )
            SELECT * FROM _Page
            WHERE _RowNum > @_Offset AND _RowNum <= @_Offset + @_PageSize
            ORDER BY _RowNum";

            await using var pageCmd = new SqlCommand(pageQuery, connection);
            pageCmd.Parameters.AddWithValue("@_Offset", offset);
            pageCmd.Parameters.AddWithValue("@_PageSize", pageSize);

            var data = await ReadResultsAsync(pageCmd, pageSize);

            // 5. Limpiar tabla temporal (la conexión cierra sola, pero es buena práctica)
            await using var dropCmd = new SqlCommand($"DROP TABLE IF EXISTS {tempName}", connection);
            await dropCmd.ExecuteNonQueryAsync();

            return (data, total);
        }
        private async Task<long> GetTotalCountAsync(
            string countQuery, List<SqlParameter> parameters)
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(countQuery, connection);
            QB.AddParametersTo(command, parameters);

            var result = await command.ExecuteScalarAsync();
            return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
        }

        private async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(
            string query, List<SqlParameter> parameters,
            int offset, int pageSize)
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.CommandTimeout = QB.CalculateDynamicTimeout(query, parameters.Count);
            QB.AddParametersTo(command, parameters);
            command.Parameters.AddWithValue("@_Offset", offset);
            command.Parameters.AddWithValue("@_PageSize", pageSize);

            return await ReadResultsAsync(command, pageSize);
        }
        private async Task<List<Dictionary<string, object?>>> ExecuteKeysetStrategyAsync(
            string selectClause, string fromClause, string whereClause,
            string groupByClause, string orderByExpression,
            int offset, int pageSize, List<SqlParameter> parameters, bool hasDistinct = false, string havingClause = "")
        {
            if (string.IsNullOrWhiteSpace(orderByExpression) || orderByExpression == "(SELECT NULL)")
            {
                // Sin ORDER BY definido, no podemos hacer keyset → fallback a CTE
                var fallbackQuery = QB.BuildPaginatedQuery(
                    selectClause, fromClause, whereClause, groupByClause,
                    "(SELECT NULL)", offset, pageSize, hasDistinct, havingClause);

                return await ExecuteQueryAsync(fallbackQuery, parameters, offset, pageSize);
            }

            await using var anchorConn = await OpenConnectionAsync();

            // Obtener el valor anchor (último valor de la página anterior)
            var anchorCol = QB.ExtractFirstOrderColumn(orderByExpression);
            var anchorQuery = $@"
                SELECT {anchorCol}
                FROM (
                    SELECT {anchorCol},
                        ROW_NUMBER() OVER (ORDER BY {orderByExpression}) AS _r
                    FROM {fromClause}
                    {whereClause}
                    {groupByClause}
                ) AS _Ordered
                WHERE _r = @_AnchorOffset";

            await using var anchorCmd = new SqlCommand(anchorQuery, anchorConn);
            anchorCmd.CommandTimeout = 30;
            QB.AddParametersTo(anchorCmd, parameters);
            anchorCmd.Parameters.AddWithValue("@_AnchorOffset", offset);

            var anchorValue = await anchorCmd.ExecuteScalarAsync();
            if (anchorValue == null || anchorValue == DBNull.Value)
                return new List<Dictionary<string, object?>>();

            // Construir query keyset
            var direction = orderByExpression.Contains("DESC", StringComparison.OrdinalIgnoreCase) ? "<" : ">";
            var keysetQuery = QB.BuildKeysetQuery(
                selectClause, fromClause, whereClause, groupByClause,
                orderByExpression, direction, anchorCol, pageSize);

            await using var dataConn = await OpenConnectionAsync();
            await using var dataCmd = new SqlCommand(keysetQuery, dataConn);
            dataCmd.CommandTimeout = QB.CalculateDynamicTimeout(keysetQuery, parameters.Count);
            QB.AddParametersTo(dataCmd, parameters);
            dataCmd.Parameters.AddWithValue("@_Anchor", anchorValue);

            return await ReadResultsAsync(dataCmd, pageSize);
        }
        protected async Task<IActionResult> ExecuteMassiveQueryAsync(
                    FiltrosRequest request,
                    string fromClause,
                    ILogger logger)
        {
            var requestId = Guid.NewGuid().ToString("N")[..8];

            try
            {
                int page = Math.Max(1, request.Page);
                int pageSize = Math.Clamp(request.PageSize, 1, 1000);
                int offset = (page - 1) * pageSize;

                if (page > 1000)
                    return BadRequest(new
                    {
                        Message = "Paginación profunda no permitida.",
                        Recommendation = "Use filtros adicionales o keyset pagination.",
                        RequestId = requestId
                    });

                // ── Normalizar aliases del request contra el FROM ───────────
                // Corrige diferencias de capitalización: "CB.Codigo" → "cb.Codigo"
                // cuando el FROM define el alias como "cb" y el cliente manda "CB".
                NormalizeRequestAliases(request, fromClause);

                // ── Construir partes del query ──────────────────────────────
                var parameters = new List<SqlParameter>();
                Console.WriteLine($"[DEBUG] RequestId={requestId} | REQUEST={request} | FROM: {fromClause}");
                var (selectClause, groupByClause, hasDistinct) = QB.BuildSelect(request);
                var whereClause = QB.BuildWhere(request, parameters);
                // HAVING: filtros sobre valores agregados (post GROUP BY)
                // useCteAliases=true porque usamos el patrón _Grouped → _Page
                var havingClause = QB.BuildHaving(request, parameters, useCteAliases: true);
                var orderBy = QB.BuildOrderBy(request);

                // ORDER BY obligatorio para ROW_NUMBER — usar fallback si no se especificó
                var orderByExpression = ResolveOrderByExpression(orderBy, selectClause, groupByClause);

                logger.LogInformation(
                    "[{Id}] Consulta masiva | FROM: {From} | Page: {Page}/{PageSize} | " +
                    "Params: {Params} | Strategy: {Strategy}",
                    requestId, fromClause[..Math.Min(60, fromClause.Length)],
                    page, pageSize, parameters.Count,
                    DeterminePaginationStrategy(groupByClause, offset, parameters.Count, pageSize).ToString());

                var strategy = DeterminePaginationStrategy(groupByClause, offset, parameters.Count, pageSize);

                logger.LogDebug(
                    "[{Id}] SELECT: {Select} | GROUP BY: {GroupBy} | WHERE: {Where} | ORDER BY: {OrderBy} | DISTINCT: {Distinct}",
                    requestId, selectClause, groupByClause, whereClause, orderByExpression, hasDistinct);

                // ── Caso especial: solo agregaciones sin GROUP BY ────────────
                // El resultado es siempre una única fila — no necesita ROW_NUMBER
                // ni paginación. Ejecutar directo para evitar el error de alias
                // en ORDER BY (los aliases de agregaciones no son válidos en OVER).
                // CONDICIÓN CORREGIDA: sin selects planos Y con agregaciones Y sin GROUP BY
                bool isSingleRowResult = string.IsNullOrEmpty(groupByClause)
                    && !request.Selects.Any(s => !string.IsNullOrWhiteSpace(s.Key))
                    && request.Agregaciones.Any(a => !string.IsNullOrWhiteSpace(a.Key))
                    && string.IsNullOrEmpty(QB.BuildOrderBy(request)); // sin ORDER BY explícito

                if (isSingleRowResult)
                {
                    var directQuery = $"SELECT {selectClause} FROM {fromClause} {whereClause} {havingClause}";
                    var directData = await ExecuteQueryDirectAsync(directQuery, parameters);
                    return Ok(new
                    {
                        RequestId = requestId,
                        Page = 1,
                        PageSize = 1,
                        TotalRecords = 1L,
                        TotalPages = 1L,
                        Strategy = "Direct",
                        Data = directData
                    });
                }

                // ── Ejecutar según estrategia ───────────────────────────────
                List<Dictionary<string, object?>> data;
                long totalRecords;

                if (strategy == PaginationStrategy.TempTable)
                {
                    (data, totalRecords) = await ExecuteTempTableStrategyAsync(
                        selectClause, fromClause, whereClause, groupByClause,
                        orderByExpression, offset, pageSize, parameters, hasDistinct, havingClause);
                }
                else if (strategy == PaginationStrategy.Keyset)
                {
                    // Keyset: lanzamos el conteo en paralelo con la query de datos
                    var countTask = GetTotalCountAsync(
                        QB.BuildCountQuery(fromClause, whereClause, groupByClause, havingClause),
                        parameters);

                    var dataTask = ExecuteKeysetStrategyAsync(
                        selectClause, fromClause, whereClause, groupByClause,
                        orderByExpression, offset, pageSize, parameters, hasDistinct, havingClause);

                    await Task.WhenAll(countTask, dataTask);
                    totalRecords = countTask.Result;
                    data = dataTask.Result;
                }
                else
                {
                    // Direct / CTE: lanzamos conteo en paralelo con datos
                    var countQuery = QB.BuildCountQuery(fromClause, whereClause, groupByClause, havingClause);
                    var dataQuery = QB.BuildPaginatedQuery(
                        selectClause, fromClause, whereClause, groupByClause,
                        orderByExpression, offset, pageSize, hasDistinct, havingClause);

                    var countTask = GetTotalCountAsync(countQuery, parameters);
                    var dataTask = ExecuteQueryAsync(dataQuery, parameters, offset, pageSize);

                    await Task.WhenAll(countTask, dataTask);
                    totalRecords = countTask.Result;
                    data = dataTask.Result;
                }

                return Ok(new
                {
                    RequestId = requestId,
                    Page = page,
                    PageSize = pageSize,
                    TotalRecords = totalRecords,
                    TotalPages = (long)Math.Ceiling((double)totalRecords / pageSize),
                    Strategy = strategy.ToString(),
                    Data = data
                });
            }
            catch (SqlException sqlEx)
            {
                logger.LogError(sqlEx, "[{Id}] Error SQL en consulta masiva | FROM: {From}",
                    requestId, fromClause);
                return StatusCode(500, new
                {
                    Message = "Error en base de datos.",
                    Details = sqlEx.Message,
                    ErrorNumber = sqlEx.Number,
                    RequestId = requestId
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Id}] Error interno en consulta masiva", requestId);
                return HandleException(ex, requestId);
            }
        }
        // Método auxiliar para encontrar un campo seguro para ordenar
        private string FindSafeOrderField(FiltrosRequest request)
        {
            // Buscar un campo ID en los selects
            var idField = request.Selects?
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Key) &&
                                   (s.Key.EndsWith(".id") || s.Key.ToLower() == "id"));

            if (idField != null)
            {
                return !string.IsNullOrWhiteSpace(idField.Alias) ? idField.Alias : idField.Key;
            }

            // Buscar cualquier campo en los selects
            var anyField = request.Selects?
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Key));

            if (anyField != null)
            {
                return !string.IsNullOrWhiteSpace(anyField.Alias) ? anyField.Alias : anyField.Key;
            }

            // Valor por defecto
            return "id";
        }
        protected string GetGroupByColumnsForCount(FiltrosRequest request)
        {
            var selectColumns = request.Selects?
                .Where(s => !string.IsNullOrWhiteSpace(s.Key))
                .Select(s =>
                    !string.IsNullOrWhiteSpace(s.Alias)
                        ? $"{s.Key} AS {s.Alias}"
                        : s.Key
                )
                .ToList();

            if (selectColumns != null && selectColumns.Any())
            {
                return $"DISTINCT {string.Join(", ", selectColumns)}";
            }
            else
            {
                // Buscar un campo ID seguro para el conteo
                var safeIdField = request.Selects?
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Key) &&
                                       (s.Key.EndsWith(".id") || s.Key == "id"))?.Key ?? "id";

                return safeIdField;
            }
        }
        protected async Task<bool> TablaExiste(string nombreTabla)
        {
            string query = @"
        SELECT COUNT(*) 
        FROM INFORMATION_SCHEMA.TABLES 
        WHERE TABLE_NAME = @NombreTabla";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@NombreTabla", nombreTabla);

            var resultado = await command.ExecuteScalarAsync();
            return Convert.ToInt32(resultado) > 0;
        }

        protected async Task<List<string>> ObtenerColumnasTabla(string nombreTabla)
        {
            var columnas = new List<string>();

            string query = @"
        SELECT COLUMN_NAME 
        FROM INFORMATION_SCHEMA.COLUMNS 
        WHERE TABLE_NAME = @NombreTabla 
        ORDER BY ORDINAL_POSITION";

            await using var connection = await OpenConnectionAsync();
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@NombreTabla", nombreTabla);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columnas.Add(reader.GetString(0).ToLower());
            }

            return columnas;
        }
        protected async Task<(bool Valid, string? Error)> ValidateFromClauseAsync(string fromClause)
        {
            if (string.IsNullOrWhiteSpace(fromClause))
                return (false, "El FROM clause no puede estar vacío.");

            // ── Capa 1: Regex ─────────────────────────────────────────────────
            // Un único patrón que captura tanto "esquema.tabla" como "tabla" sola.
            // Grupo 1 (opcional): esquema   Grupo 2: nombre de tabla
            //
            // Cubre todos los tipos de JOIN: INNER, LEFT, RIGHT, FULL, CROSS
            // y la cláusula FROM inicial.
            // Patrón en tres alternativas (grupos por pares: schema+tabla):
            //   Grupos (1,2) → tabla principal al inicio del string
            //   Grupos (3,4) → tablas de JOIN tipificado (INNER/LEFT/RIGHT/FULL/CROSS)
            //   Grupos (5,6) → tablas de JOIN simple
            // El esquema es siempre opcional; la tabla siempre presente en el segundo del par.
            const string tablePattern =
                @"^\s*\[?(?:([a-zA-Z0-9_]+)\]?\.\[?)?([a-zA-Z0-9_]+)\]?" +
                @"|(?:INNER|LEFT|RIGHT|FULL|CROSS)\s+(?:OUTER\s+)?JOIN\s+\[?(?:([a-zA-Z0-9_]+)\]?\.\[?)?([a-zA-Z0-9_]+)\]?" +
                @"|\bJOIN\s+\[?(?:([a-zA-Z0-9_]+)\]?\.\[?)?([a-zA-Z0-9_]+)\]?";

            var tableMatches = System.Text.RegularExpressions.Regex.Matches(
                fromClause,
                tablePattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);

            if (!tableMatches.Any())
                return (false, "No se pudo extraer ningún identificador de tabla del FROM clause.");

            var tableNames = new List<(string? Schema, string Table)>();

            foreach (System.Text.RegularExpressions.Match m in tableMatches)
            {
                // Recorrer los tres pares de grupos (schema, tabla)
                var groupPairs = new[] { (1, 2), (3, 4), (5, 6) };
                foreach (var (schemaGroup, tableGroup) in groupPairs)
                {
                    if (!m.Groups[tableGroup].Success || string.IsNullOrWhiteSpace(m.Groups[tableGroup].Value))
                        continue;

                    var schema = m.Groups[schemaGroup].Success && !string.IsNullOrWhiteSpace(m.Groups[schemaGroup].Value)
                        ? m.Groups[schemaGroup].Value
                        : null;
                    tableNames.Add((schema, m.Groups[tableGroup].Value));
                    break;
                }
            }

            // Verificar que los nombres extraídos solo tengan caracteres válidos
            var identifierRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9_]+$");
            foreach (var (schema, table) in tableNames)
            {
                if (!identifierRegex.IsMatch(table))
                    return (false, $"Nombre de tabla inválido: '{table}'.");

                if (schema != null && !identifierRegex.IsMatch(schema))
                    return (false, $"Nombre de esquema inválido: '{schema}'.");
            }

            // ── Capa 2: INFORMATION_SCHEMA ────────────────────────────────────
            const string sql = @"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = @TableName
                  AND (@Schema IS NULL OR TABLE_SCHEMA = @Schema)";

            await using var conn = await OpenConnectionAsync();

            foreach (var (schema, table) in tableNames)
            {
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@TableName", table);
                cmd.Parameters.AddWithValue("@Schema", (object?)schema ?? DBNull.Value);

                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                    return (false, $"La tabla '{(schema != null ? $"{schema}.{table}" : table)}' no existe en la base de datos.");
            }

            return (true, null);
        }
        protected static (bool Valid, string? Error) ValidateIdentifier(string identifier, string label = "Identificador")
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return (false, $"{label} no puede estar vacío.");

            // Permitir esquema opcional: schema.nombre
            var parts = identifier.Split('.');
            if (parts.Length > 2)
                return (false, $"{label} '{identifier}' tiene un formato inválido.");

            var identifierRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9_]+$");
            foreach (var part in parts)
            {
                if (!identifierRegex.IsMatch(part))
                    return (false, $"{label} '{identifier}' contiene caracteres no permitidos.");
            }

            return (true, null);
        }
        protected enum PaginationStrategy { Direct, TempTable, Keyset }

        protected PaginationStrategy DeterminePaginationStrategy(
            string groupByClause, int offset, int paramCount, int pageSize = 10)
        {
            // Las queries de solo-agregaciones (comparación, stats) se resuelven
            // siempre como Direct antes de llegar aquí — nunca necesitan TempTable.
            // Aún así protegemos: si hay GROUP BY, Direct/CTE es siempre correcto.

            // Las queries de stats mandan pageSize=1 — nunca necesitan TempTable ni Keyset.
            // Esto también protege el caso donde isSingleRowResult no capturó la query.
            if (pageSize <= 1)
                return PaginationStrategy.Direct;

            // Keyset para offsets muy grandes sin GROUP BY
            // (con GROUP BY el CTE _Grouped → _Page es más seguro)
            if (offset >= 20000 && string.IsNullOrEmpty(groupByClause))
                return PaginationStrategy.Keyset;

            // TempTable solo para consultas SIN GROUP BY con offset moderado-alto.
            // Umbral de parámetros elevado a 20 para evitar que las queries de
            // comparación (hasta 18 params por sub-query) caigan en TempTable y
            // generen conflictos de #Tmp_ entre peticiones concurrentes.
            bool largeOffsetNoGroup = offset >= 5000 && string.IsNullOrEmpty(groupByClause);
            bool heavyQueryNoGroup = paramCount >= 20 && string.IsNullOrEmpty(groupByClause);

            if (largeOffsetNoGroup || heavyQueryNoGroup)
                return PaginationStrategy.TempTable;

            // Default: CTE con ROW_NUMBER — maneja correctamente GROUP BY,
            // HAVING, DISTINCT y cualquier combinación de agregaciones.
            return PaginationStrategy.Direct;
        }
        private static string StripTableAlias(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return identifier;

            // Ya es un alias puro entre corchetes sin punto → devolver tal cual
            if (identifier.StartsWith("[") && !identifier.Contains('.')) return identifier;

            // Contiene punto → tomar solo la parte después del último punto
            if (identifier.Contains('.'))
            {
                var parts = identifier.Split('.');
                var last = parts[^1].Trim('[', ']').Trim();
                return string.IsNullOrWhiteSpace(last) ? identifier : last;
            }

            return identifier;
        }

        protected static string NormalizeSimpleOperator(string? op) =>
            op?.ToUpperInvariant() switch
            {
                "LIKE" => "LIKE",
                ">=" => ">=",
                "<=" => "<=",
                ">" => ">",
                "<" => "<",
                "<>" => "<>",
                "!=" => "<>",
                _ => "="
            };

    }
}
