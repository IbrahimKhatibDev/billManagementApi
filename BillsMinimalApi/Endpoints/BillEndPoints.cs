using BillsMinimalApi.Contracts;
using BillsMinimalApi.Data;
using BillsMinimalApi.Dtos;
using BillsMinimalApi.Mappers;
using BillsMinimalApi.Models;
using BillsMinimalApi.Parsing;
using BillsMinimalApi.Queries;
using Microsoft.EntityFrameworkCore;

namespace BillsMinimalApi.Endpoints
{
    public static class BillEndPoints
    {
        public static void MapBillEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/restapi/BillDtos")
                       .WithTags("Bills");

            // GET A PAGE
            //
            // This used to be `db.Bills.ToListAsync()` — every row, every time,
            // with the client left to filter, sort and paginate it. The work now
            // happens in Postgres: the filters below compose into a single
            // statement whose LIMIT is what bounds the response, and pageSize is
            // clamped in BillQuery.Parse so that asking for the whole table is
            // not an option a client has.
            group.MapGet("/", async (
                AppDbContext db,
                CancellationToken cancellationToken,
                int? page = null,
                int? pageSize = null,
                string? search = null,
                string? status = null,
                string? sort = null,
                string? dir = null,
                DateTime? from = null,
                DateTime? to = null) =>
            {
                var query = BillQuery.Parse(page, pageSize, search, status, sort, dir, from, to);

                // Read once and pass it down: "overdue" is a comparison against
                // today, and a request that straddled midnight would otherwise
                // count with one date and page with another.
                var today = UtcDateTime.Today;

                var filtered = db.Bills
                    .AsNoTracking()
                    .ApplyFilters(query, today);

                var totalCount = await filtered.CountAsync(cancellationToken);

                // Clamp to the last page rather than serving an empty one. The
                // set can shrink under a client that is standing on page 5 —
                // a delete, or another user's write — and an empty table with
                // "showing 0 of 40" reads as a bug.
                var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)query.PageSize));
                var currentPage = Math.Clamp(query.Page, 1, totalPages);

                // Materialise the page first, then map. BillMapper.ToDto is a
                // method call EF cannot translate, and inlining the projection
                // here instead would leave two definitions of what a BillDto is.
                var rows = await filtered
                    .ApplySort(query)
                    .Skip((currentPage - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToListAsync(cancellationToken);

                return Results.Ok(new PagedResult<BillDto>
                {
                    Items = rows.Select(BillMapper.ToDto).ToList(),
                    Page = currentPage,
                    PageSize = query.PageSize,
                    TotalCount = totalCount,
                });
            });

            // GET THE REPORT
            //
            // Before the id route, or "/summary" would be tried against
            // "/{id:long}" first. It does not match — the constraint sees to
            // that — but relying on a route constraint to disambiguate two
            // endpoints is a fragile thing to leave lying around.
            group.MapGet("/summary", async (
                AppDbContext db,
                CancellationToken cancellationToken,
                DateTime? from = null,
                DateTime? to = null) =>
            {
                var summary = await BillSummaryBuilder.BuildAsync(
                    db, from, to, UtcDateTime.Today, cancellationToken);

                return Results.Ok(summary);
            });

            // PARSE A LINE OF TEXT
            //
            // Reads and returns; it never touches the DbContext. The client
            // renders the reading, the user corrects it, and the bill is created
            // through POST "/" like any other — so a misread costs a keystroke
            // rather than a row that has to be found and fixed.
            //
            // Today is read here rather than in the parser so that "fri" is
            // resolved against the server's clock, which is the same clock every
            // other date comparison in this API uses.
            group.MapPost("/parse", (ParseBillRequest request) =>
                Results.Ok(BillTextParser.Parse(request.Text, UtcDateTime.Today)));

            // GET BY ID
            group.MapGet("/{id:long}", async (long id, AppDbContext db) =>
            {
                var bill = await db.Bills.FindAsync(id);
                return bill is null ? Results.NotFound() : Results.Ok(BillMapper.ToDto(bill));
            });

            // POST
            group.MapPost("/", async (BillDto dto, AppDbContext db) =>
            {
                // ToNewEntity, not ToEntity: a client-supplied Id must never
                // reach the identity column. See BillMapper for why.
                var entity = BillMapper.ToNewEntity(dto);
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

                BillMapper.ApplyEditableFields(dto, existing);

                // Optimistic concurrency. EF builds the UPDATE's WHERE clause
                // from the *original* value of the concurrency token, and
                // FindAsync just loaded it microseconds ago — so assigning
                // existing.Version alone can never conflict. Overwriting
                // OriginalValue with what the client believed the version to be
                // is what makes the check real.
                db.Entry(existing).Property(b => b.Version).OriginalValue = dto.Version;
                existing.Version = dto.Version + 1;

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Results.Conflict("The data has changed since your last read.");
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
