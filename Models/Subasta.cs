namespace MyApiProject.Models
{
    public class Ofeta_Proveedores
    {
        public string precio_oferta { get; set; }
        public int subasta_compra_id { get; set; }
        public int proveedor_id { get; set; }
        public DateTime fecha_oferta { get; set; }
    }
    public class Ofeta_ProveedoresUpdate
    {
        public string? precio_oferta { get; set; }
        public int? subasta_compra_id { get; set; }
        public int? proveedor_id { get; set; }
        public DateTime? fecha_oferta { get; set; }
    }

}