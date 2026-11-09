# Alto Bank

A banking web app built with ASP.NET Core MVC (.NET 8), Entity Framework Core (SQLite) and Bootstrap.
It has a **customer bank** and a separate **admin portal**. Everything is fictional (class project).

## Requirements

- .NET 8 SDK. Check with `dotnet --list-sdks`.

## Run the app

```bash
cd AltoBank
dotnet tool restore
dotnet run
```

Open **http://localhost:5264**

The database (`altobank.db`) is created and filled with demo data automatically the first time.

## Log in

On the login page, click **Demo Customer** or **Demo Admin**, or type:

| Who | Email | Password |
|---|---|---|
| Customer | demo@altobank.com | Demo123! |
| Customer | maya@altobank.com | Demo123! |
| Customer | sam@altobank.com | Demo123! |
| Admin | admin@altobank.com | Admin123! |

- Customers land on **/dashboard**.
- Admins land on **/admin**.

## Seed the database

```bash
cd AltoBank
dotnet run -- --seed       # add any missing demo data, then exit (safe to run again)
dotnet run -- --reset-db   # delete the database and start fresh with demo data, then exit
```

## Database migrations

```bash
cd AltoBank
dotnet ef migrations add <Name>   # after changing a model
dotnet ef database update         # apply migrations
```

## Useful pages

| Page | URL |
|---|---|
| Home | http://localhost:5264 |
| Customer dashboard | http://localhost:5264/dashboard |
| Admin portal | http://localhost:5264/admin |
| API docs (Swagger) | http://localhost:5264/swagger |

## More detail

See [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) for:
- where each course requirement is used in the code
- the full list of API endpoints
