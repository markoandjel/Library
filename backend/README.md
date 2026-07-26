# Library .NET Backend

ASP.NET Core application backed by MySQL and deployed with Docker Compose.

## Local or server deployment

From the repository root:

```bash
cp .env.example .env
nano .env

docker compose config
docker compose up -d --build
```

Set every password and SFTP value in `.env` before starting the application. The
file is ignored by Git. The application is available at
`http://SERVER_IP:APP_PORT`; MySQL is only reachable inside the private Compose
network.

Useful commands:

```bash
docker compose ps
docker compose logs --tail=100 library
docker compose logs --tail=100 database
curl http://127.0.0.1:5098/health
```

The SQL dump mounted under `/docker-entrypoint-initdb.d/` is imported only when
the MySQL data volume is created for the first time.

## API

The API exposes:

- `GET /health`
- CRUD for `/authors`, `/books`, `/cabinets`, `/categories`, `/languages`, `/letters`, `/publishers`, `/shelves`

`/books` accepts and returns relation ID lists including `kategorijaIds`,
`autorIds`, `jezikIds`, `jezikOrigIds`, and `pismoIds`.

Swagger is available at `/swagger` only in Development.
