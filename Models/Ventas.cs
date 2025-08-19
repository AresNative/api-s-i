namespace MyApiProject.Models
{
    public class Articulo
    {
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public decimal Precio { get; set; }
        public int categoria_id { get; set; }
        public int unidad_id { get; set; }

        // Otros campos relevantes para el modelo de Articulo
    }
    public class ArticuloUpdate
    {
        public int? id { get; set; }
        public string? Nombre { get; set; }
        public string? Descripcion { get; set; }
        public decimal? Precio { get; set; }
        public int? categoria_id { get; set; }
        public int? unidad_id { get; set; }

        // Otros campos relevantes para el modelo de Articulo
    }
    public class Productos_nuevos
    {
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public int categoria_id { get; set; }
        public DateTime fecha_solicitud { get; set; }
        public string estado { get; set; }

        // Otros campos relevantes para el modelo de Productos_nuevos
    }
    public class Productos_nuevosUpdate
    {
        public int? id { get; set; }
        public string? Nombre { get; set; }
        public string? Descripcion { get; set; }
        public int? categoria_id { get; set; }
        public DateTime? fecha_solicitud { get; set; }
        public string? estado { get; set; }

        // Otros campos relevantes para el modelo de Productos_nuevos
    }
    public class Categoria
    {

        public string Nombre { get; set; }

        // Otros campos relevantes para el modelo de Categoria
    }
    public class CategoriaUpdate
    {
        public int? id { get; set; }
        public string? Nombre { get; set; }

        // Otros campos relevantes para el modelo de Categoria
    }
    public class Unidades
    {
        public string Nombre { get; set; }
        public string Simbolo { get; set; }

        // Otros campos relevantes para el modelo de Unidad
    }
    public class UnidadesUpdate
    {
        public int? id { get; set; }
        public string Nombre { get; set; }
        public string Simbolo { get; set; }

        // Otros campos relevantes para el modelo de Unidad
    }
    public class Codigos_Barras
    {
        public int articulo_id { get; set; }
        public string codigo_barras { get; set; }

        // Otros campos relevantes para el modelo de Codigos_Barras
    }
    public class Codigos_BarrasUpdate
    {
        public int? id { get; set; }
        public int? articulo_id { get; set; }
        public string? codigo_barras { get; set; }

        // Otros campos relevantes para el modelo de Codigos_Barras
    }

}