# API de Supermercados Mejia

Api de uso general protegida por el uso de tokens

## Environment Variables 📌

El proyecto al ser en .net 8 no existe un archivo .env pero si existe seccion de configuracion en -> [appsettings.json](https://github.com/AresNative/api/blob/v2/appsettings.json)

"DefaultConnection": `CONTECTION_BD_SQLSERVER`

_key inicial para el uso de JWT_ "Key" : `API_KEY`

_key PUBLICA para JWT no obligatoria_ "PublicKey": `ANOTHER_API_KEY`

## Características

- Endpoints con paginado dinamico
- Filtros escalables
- Modelos descriptivos
- Insterts escalables

## Tecnologias

**Client:** NextJS, Ionic, React, Redux

**Server:** .NET 8, NodeJs, Express

## Usado en

Este proyecto lo utilizan las siguientes paginas:

- [mercadosliz.com](https://mercadosliz.com)

- [admin.mercadosliz.com](https://admin.mercadosliz.com)

## Comentarios

Si tiene algún comentario, comuníquese con nosotros en sistemas02@mercadosliz.com

## Soporte

Para recibir asistencia, envíe un correo electrónico a sistemas02@mercadosliz.com

## Instalacion

Clonar proyecto

```bash
  git clone https://github.com/AresNative/api.git --depth=1
```

Ir a la direccion creada

```bash
  cd api
```

Limpiar dependencias

```bash
  dotnet clean
```

Instalar dependencias

```bash
  dotnet build
```

Instalar dependencias personalizadas

```bash
  dotnet add package 'tu dependencia o libreria'
```

Iniciar api

```bash
  dotnet run
```

Actualizar ssl

```bash
    cd  C:\Win-ACME
```

```bash
    wacs.exe --source manual --host api.mercadosliz.com
```

## Certificado necesarios

Instalar un certificado de desarrollo HTTPS de ASP.NET Core.

```bash
    dotnet dev-certs https --trust
```

## Publicar

```bash
  dotnet publish  -o ./si
```

## Referencaias

[![desarollador](https://img.shields.io/badge/my_portfolio-000?style=for-the-badge&logo=ko-fi&logoColor=white)](https://eusebio.dev/)
