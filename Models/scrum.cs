
using MyApiProject.Models;
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
    public string NuevoEstado { get; set; }
}

public class HistorialTareaRequest
{
    public int TareaId { get; set; }
    public string DescripcionCambio { get; set; }
}

public class ComentarioRequest
{
    public int TareaId { get; set; }
    public int UsuarioId { get; set; }
    public string Contenido { get; set; }
}

public class UploadArchivoTarea
{
    public int TareaId { get; set; }
    public string Descripcion { get; set; }
    public IFormFile File { get; set; }
}