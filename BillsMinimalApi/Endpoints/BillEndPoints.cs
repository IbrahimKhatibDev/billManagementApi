using BillsMinimalApi.Data;
using BillsMinimalApi.Dtos;
using BillsMinimalApi.Mappers;
using BillsMinimalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BillsMinimalApi.Endpoints
{
    public static class BillEndPoints
    {
        public static void MapBillEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/restapi/BillDtos")
                       .WithTags("Bills");

            // GET ALL
            group.MapGet("/", async (AppDbContext db) =>
            {
                var bills = await db.Bills.ToListAsync();
                return Results.Ok(bills.Select(BillMapper.ToDto));
            });

            // GET BY ID
            group.MapGet("/{id:long}", async (long id, AppDbContext db) =>
            {
                var bill = await db.Bills.FindAsync(id);
                return bill is null ? Results.NotFound() : Results.Ok(BillMapper.ToDto(bill));
            });

            // POST
            group.MapPost("/", async (BillDto dto, AppDbContext db) =>
            {
                var entity = BillMapper.ToEntity(dto);
                db.Bills.Add(entity);
                await db.SaveChangesAsync();

                return Results.Created($"/restapi/BillDtos/{entity.Id}", BillMapper.ToDto(entity));
            });

            // PUT
            group.MapPut("/{id:long}", async (long id, BillDto dto, AppDbContext db) =>
            {
                if (id != dto.Id)
                    return Results.BadRequest("ID mismatch");

                var existing = await db.Bills.FindAsync(id);
                if (existing is null)
                    return Results.NotFound();

                existing.PayeeName = dto.PayeeName;
                existing.DueDate = dto.DueDate;
                existing.PaymentDue = dto.PaymentDue;
                existing.Paid = dto.Paid;
                existing.Version = dto.Version;

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Results.BadRequest("The data has changed since your last read.");
                }

                return Results.Ok(BillMapper.ToDto(existing));
            });

            // DELETE
            group.MapDelete("/{id:long}", async (long id, AppDbContext db) =>
            {
                var existing = await db.Bills.FindAsync(id);
                if (existing is null) return Results.NotFound();

                db.Bills.Remove(existing);
                await db.SaveChangesAsync();

                return Results.NoContent();
            });
        }
    }
}
