using Newtonsoft.Json.Linq;

namespace MyApiProject.Models
{
    /// Request para el endpoint PUT /update/{tabla}.
    ///
    /// Filtros → lista plana de condiciones AND para el WHERE.
    /// Data    → objeto JSON libre con los campos a actualizar.
    ///
    /// Ejemplo:
    /// {
    ///   "Filtros": [
    ///     { "Key": "id", "Operator": "=", "Value": "42" }
    ///   ],
    ///   "Data": {
    ///     "Status": "A",
    ///     "FechaModificacion": "2024-06-01"
    ///   }
    /// }
    public class ActualizarRequest
    {
        public List<BusquedaParams> Filtros { get; set; } = new();
        public JObject? Data { get; set; }
    }
}