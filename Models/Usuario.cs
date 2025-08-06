namespace MyApiProject.Models
{
    public class Usuario
    {
        public string email { get; set; }
        public string password { get; set; }
        public string rol { get; set; }
    }
    public class UsuarioUpdate
    {
        public string? email { get; set; }
        public string? password { get; set; }
        public string? rol { get; set; }
    }
    public class PostulacionData
    {
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public string Email { get; set; }
        public string Telefono { get; set; }
        public string CV { get; set; } // Assuming this is a base64 encoded string of the CV file
    }
    public class UploadPostulacion
    {
        public string PostulacionData { get; set; }
        public IFormFile File { get; set; }
    }

    public class empleados
    {
        public string nombre { get; set; }
        public string apellido { get; set; }
        public string email { get; set; }
        public string telefono { get; set; }
        public string direccion { get; set; }
        public string fecha_nacimiento { get; set; }
        public string fecha_ingreso { get; set; }
        public string puesto { get; set; }
        public string departamento { get; set; }
        public string salario { get; set; }
        public string estado { get; set; }
        public string rfc { get; set; }
        public string curp { get; set; }
        public string nss { get; set; }
        public string cuenta_bancaria { get; set; }
        public string banco { get; set; }
        public string clabe { get; set; }
        public string usuario_id { get; set; }
    }
}
