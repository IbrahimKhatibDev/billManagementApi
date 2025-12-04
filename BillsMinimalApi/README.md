# BillsMinimalApi – Backend REST API (Minimal API + .NET 8 + SQL Server)

It fully supports CRUD operations, DTO mapping, EF Core with SQL Server, Swagger/OpenAPI documentation, and Bogus-based database seeding.

The backend is lightweight, fast, and easy to use with any frontend framework (React, Blazor, Plain JavaScript, etc.).

## Framework Choice

### Why .NET 8 Minimal API?

I chose ASP.NET Core Minimal API because:

- It provides the simplest way to build REST endpoints.
- Similar to Jakarta REST endpoints.
- Works naturally with EF Core for SQL Server.
- Default project template includes Swagger.
- It's a language I've used before and am familiar with.

## Project Structure
<img width="350" height="467" alt="image" src="https://github.com/user-attachments/assets/fbc28f17-62ed-4d15-a4c4-349e2255ac55" />

## Database

The API uses Microsoft SQL Server LocalDB by default.
Connection string (in appsettings.json):

```
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=BillDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
}
```
You can switch to full SQL Server or Docker SQL by updating this value.

## Setup Instructions

### Prerequisites
- .NET SDK 8.0
- SQL Server LocalDB (installed with Visual Studio)
- Visual Studio 2022 or VS Code
- EF Core CLI tools (installed automatically)

### 1. Clone the repository
```
git clone <https://github.com/DMIT-2015/dmit2015-1251-courseproject-IbrahimKhatibDev.git>
cd BillsMinimalApi
```

### 2. Restore packages
```
dotnet restore
```

### 3. Apply EF Core migrations

If the database does not exist yet:
```
Add-Migration InitialCreate
Update-Database
```
To reset database:

Delete BillDb in SQL Object Explorer

Run:
```
Update-Database
```

### 4. Run the application

In Visual Studio:
F5

## How to Test the Backend + Key Endpoints (CRUD)

### REST API Endpoints
 ```
/restapi/BillDtos
```

### GET — Get all bills
 ```
group.MapGet("/", async (AppDbContext db) =>
{
    var bills = await db.Bills.ToListAsync();
    return Results.Ok(bills.Select(BillMapper.ToDto));
});
```

Test in Swagger:
 ```
GET /restapi/BillDtos
```

### GET — Get a bill by ID
 ```
group.MapGet("/{id:long}", async (long id, AppDbContext db) =>
{
    var bill = await db.Bills.FindAsync(id);
    return bill is null ? Results.NotFound() : Results.Ok(BillMapper.ToDto(bill));
});
```

Example:
 ```
GET /restapi/BillDtos/1
```

### POST — Create a new bill
 ```
group.MapPost("/", async (BillDto dto, AppDbContext db) =>
{
    var entity = BillMapper.ToEntity(dto);
    db.Bills.Add(entity);
    await db.SaveChangesAsync();

    return Results.Created($"/restapi/BillDtos/{entity.Id}", BillMapper.ToDto(entity));
});
```

Example JSON:
 ```
{
  "payeeName": "Rogers",
  "dueDate": "2025-01-15",
  "paymentDue": 120.50,
  "paid": false,
  "version": 1
}
```

### PUT — Update an existing bill
 ```
group.MapPut("/{id:long}", async (long id, BillDto dto, AppDbContext db) =>
{
    if (id != dto.Id) return Results.BadRequest("ID mismatch");

    var existing = await db.Bills.FindAsync(id);
    if (existing is null) return Results.NotFound();

    existing.PayeeName = dto.PayeeName;
    existing.DueDate = dto.DueDate;
    existing.PaymentDue = dto.PaymentDue;
    existing.Paid = dto.Paid;
    existing.Version = dto.Version;

    await db.SaveChangesAsync();
    return Results.Ok(BillMapper.ToDto(existing));
});
```

### DELETE — Delete a bill:
 ```
group.MapDelete("/{id:long}", async (long id, AppDbContext db) =>
{
    var existing = await db.Bills.FindAsync(id);
    if (existing is null) return Results.NotFound();

    db.Bills.Remove(existing);
    await db.SaveChangesAsync();

    return Results.NoContent();
});
```

Example:
 ```
DELETE /restapi/BillDtos/5
```

## Swagger Testing

When running the backend, go to:
 ```
https://localhost:<port>/swagger
```

You can:
- View all endpoints
- Send test requests
- Verify JSON responses
- Confirm CRUD operations work correctly

## Database Seeding

At startup, 10 fake Bill records are inserted if the table is empty.
This uses the Bogus library for test data.
