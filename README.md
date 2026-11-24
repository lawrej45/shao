# Instructor Information System

This is an ASP.NET Core 8 MVC web application. Open it in Chrome or another browser at the URL where the server is running. It cannot be hosted as static files only; the host must support ASP.NET Core 8 or Docker containers.

## Run locally

Install the .NET 8 SDK, then from this directory run:

```sh
dotnet run
```

Open the URL printed by the command and go to `/Account/Login` (for example, `http://localhost:5000/Account/Login`).

## Build and run with Docker

From this directory:

```sh
docker build -t instructor-information-system .
docker run --rm -p 8080:8080 -v instructor-data:/data -e ConnectionStrings__DefaultConnection="Data Source=/data/instructors.db" instructor-information-system
```

Open `http://localhost:8080/Account/Login` in Chrome. The named Docker volume keeps the SQLite database when the container is replaced. Configure the host's public port and persistent volume as needed. Set `ConnectionStrings__DefaultConnection` to a writable persistent SQLite path, or configure an appropriate connection string for the hosting environment.

The host must provide HTTPS in production because authentication cookies are marked secure outside development. Configure HTTPS termination and forwarded headers according to the host's ASP.NET Core setup.
