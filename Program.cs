using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MyApiProject.Hubs;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Configuración de CORS optimizada
var allowedCorsOrigins = configuration.GetSection("AllowedCorsOrigins").Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowedCorsOrigins", policy =>
    {
        policy.WithOrigins(allowedCorsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Configuración de autenticación JWT optimizada
var jwtSettings = configuration.GetRequiredSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? throw new InvalidOperationException("JWT Key is missing"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
        options.SaveToken = true;
    });

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null; // Mantiene nombres originales de las propiedades
        options.JsonSerializerOptions.MaxDepth = 64; // Un valor más razonable para evitar problemas de rendimiento
    })
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.MaxDepth = 64; // Limita la profundidad para evitar sobrecarga
        options.SerializerSettings.Error = (sender, args) => args.ErrorContext.Handled = true;
    });

// Registro de servicios
builder.Services.AddHttpClient();
builder.Services.AddScoped<AuthUtils>();
builder.Services.AddScoped<ScrumUtils>();
builder.Services.AddScoped<TokensUtils>();
builder.Services.AddScoped<FilterUtils>();

// Registrar IMemoryCache
builder.Services.AddMemoryCache(); // Esto es necesario para resolver IMemoryCache

// ↓↓↓ Agregar SignalR a los servicios ↓↓↓
builder.Services.AddSignalR();

// Configuración de Swagger con seguridad JWT optimizada
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("general", new OpenApiInfo { Title = "Solucion integral", Version = "v1" });
    c.SwaggerDoc("users", new OpenApiInfo { Title = "Users", Version = "v1" });
    c.SwaggerDoc("scrum", new OpenApiInfo { Title = "Scrum", Version = "v1" });
    c.SwaggerDoc("ventas", new OpenApiInfo { Title = "Ventas", Version = "v1" });
    c.SwaggerDoc("subasta", new OpenApiInfo { Title = "Subasta", Version = "v1" });
    c.OperationFilter<FileUploadOperationFilter>();

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Ingrese el token JWT en este formato: Bearer {token}",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

var app = builder.Build();

// Configuración del pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/general/swagger.json", "Solucion integral v1");
    c.SwaggerEndpoint("/swagger/users/swagger.json", "Users v1");
    c.SwaggerEndpoint("/swagger/scrum/swagger.json", "Scrum v1");
    c.SwaggerEndpoint("/swagger/ventas/swagger.json", "Ventas v1");
    c.SwaggerEndpoint("/swagger/subasta/swagger.json", "Subasta v1");
    c.RoutePrefix = string.Empty; // Acceso a Swagger en la raíz
});

app.UseCors("AllowedCorsOrigins");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<Hubs>("/Hubs");
app.MapControllers();
app.Run();
