// Models/FiltrosRequest.cs
namespace MyApiProject.Models
{
    // ── Proyección ───────────────────────────────────────────────────────────

    public class SelectItem
    {
        public string Key { get; set; } = string.Empty;
        public string? Alias { get; set; }
    }
    public class AgregacionItem
    {
        public string Key { get; set; } = string.Empty;
        public string? Operation { get; set; }
        public string? Alias { get; set; }
    }

    // ── Filtros ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Filtro individual.
    ///   - BETWEEN:      Value = "valor1 AND valor2"
    ///   - IN / NOT IN:  Value = "v1,v2,v3"
    ///   - TIME_BETWEEN: Value = "08:00:00 AND 17:00:00"
    ///   - CASE_WHEN:    Value = JSON { "when":[{"condition":"...","then":"..."}], "else":"..." }
    ///   - IS NULL / IS NOT NULL: Value puede omitirse
    /// </summary>
    public class BusquedaParams
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// = | != | &lt;&gt; | &gt; | &gt;= | &lt; | &lt;= | LIKE |
        /// IN | NOT IN | BETWEEN | NOT BETWEEN |
        /// IS NULL | IS NOT NULL | TIME_BETWEEN | CASE_WHEN
        /// </summary>
        public string? Operator { get; set; } = "=";
    }

    /// <summary>
    /// Grupo de filtros con operador lógico interno.
    /// Los miembros del grupo se unen con OperadorLogico (AND u OR).
    /// Los grupos entre sí siempre se combinan con AND al WHERE principal.
    /// </summary>
    public class GrupoFiltros
    {
        /// <summary>AND | OR — operador entre los filtros dentro del grupo</summary>
        public string OperadorLogico { get; set; } = "AND";

        public List<BusquedaParams> Filtros { get; set; } = new();
    }

    // ── Ordenamiento ─────────────────────────────────────────────────────────

    public class OrderItem
    {
        public string Key { get; set; } = string.Empty;
        public string Direction { get; set; } = "ASC";
    }
    public class FiltrosRequest
    {
        public List<SelectItem> Selects { get; set; } = new();
        public List<AgregacionItem> Agregaciones { get; set; } = new();
        public List<BusquedaParams> Filtros { get; set; } = new();
        public List<GrupoFiltros> FiltrosAnd { get; set; } = new();
        public List<GrupoFiltros> FiltrosOr { get; set; } = new();

        public List<OrderItem> Order { get; set; } = new();
        public List<BusquedaParams> Having { get; set; } = new();

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}