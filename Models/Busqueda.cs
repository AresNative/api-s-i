namespace MyApiProject.Models
{
    public class BusquedaParams
    {
        public string? Key { get; set; }
        public string? Value { get; set; }
        public string? Operator { get; set; }  // Opcional: puedes usar operadores como 'like', '=', '>=', etc.
    }
    public class SumaParams
    {
        public string? Key { get; set; }
    }
    public class SumaAsParams
    {
        public string? Key { get; set; }
        public string? Alias { get; set; }  // Alias para la suma, por ejemplo: "SUM(Cantidad) AS TotalCantidad"
    }
    public class OrderParams
    {
        public string? Key { get; set; }
        public string? Direction { get; set; }  // "ASC" o "DESC"
    }
    public class FiltrosRequest
    {
        public List<BusquedaParams> Filtros { get; set; } = new();
        public List<SumaParams> Selects { get; set; } = new();
        public List<OrderParams> Order { get; set; } = new();
    }
}