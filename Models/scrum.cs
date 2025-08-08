namespace MyApiProject.Models
{
    public class UploadProyecto
    {
        public string ProyectoData { get; set; }
        public IFormFile File { get; set; }
    }

    public class ProyectosRequest
    {
        public List<BusquedaParams> Filtros { get; set; } = new();
    }

    public class EstadoTareaRequest
    {
        public string estado { get; set; }
    }

    public class HistorialTareaRequest
    {
        public int tarea_id { get; set; }
        public string descripcion_cambio { get; set; }
    }
    public class Proyectos
    {
        public string nombre { get; set; }
        public string descripcion { get; set; }
        public DateTime fecha_inicio { get; set; }
        public DateTime fecha_fin { get; set; }
    }
    public class ProyectosUpdate
    {
        public string? nombre { get; set; }
        public string? descripcion { get; set; }
        public DateTime? fecha_inicio { get; set; }
        public DateTime? fecha_fin { get; set; }
    }
    public class Sprints
    {
        public string nombre { get; set; }
        public DateTime fecha_inicio { get; set; }
        public DateTime fecha_fin { get; set; }
        public int proyecto_id { get; set; }
    }
    public class SprintsUpdate
    {
        public string? nombre { get; set; }
        public DateTime? fecha_inicio { get; set; }
        public DateTime? fecha_fin { get; set; }
        public int? proyecto_id { get; set; }
    }
    public class Tarea
    {
        public string titulo { get; set; }
        public string descripcion { get; set; }
        public string estado { get; set; }
        public DateTime fecha_creacion { get; set; }
        public DateTime? fecha_entrega { get; set; }
        public int sprint_id { get; set; }
        public string prioridad { get; set; }
    }
    public class TareaUpdate
    {
        public string? titulo { get; set; }
        public string? descripcion { get; set; }
        public string? estado { get; set; }
        public DateTime? fecha_creacion { get; set; }
        public DateTime? fecha_entrega { get; set; }
        public int? sprint_id { get; set; }
        public string? prioridad { get; set; }
    }
    public class Comentarios
    {
        public int tarea_id { get; set; }
        public int usuario_id { get; set; }
        public string contenido { get; set; }
        public DateTime fecha { get; set; }
    }
    public class ComentariosUpdate
    {
        public int? tarea_id { get; set; }
        public int? usuario_id { get; set; }
        public string? contenido { get; set; }
        public DateTime? fecha { get; set; }
    }
    public class UploadArchivoTarea
    {
        public int tarea_id { get; set; }
        public string descripcion { get; set; }
        public IFormFile File { get; set; }
    }
}