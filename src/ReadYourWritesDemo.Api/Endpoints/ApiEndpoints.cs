using System.Security.Claims;
using Npgsql;
using ReadYourWritesDemo.Api.Models;
using ReadYourWritesDemo.Api.Services;

namespace ReadYourWritesDemo.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api");


        group.MapGet("/me", async (HttpContext ctx, DbRouter router) =>
        {
            var userId = GetUserId(ctx);
            await using var conn = await router.GetConnectionForReadAsync(requiresReadYourWrites: true);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT email, name, avatar_url FROM users WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Results.NotFound();

            var dto = new UserProfileDto(
                Email: reader.GetString(0),
                Name: reader.GetString(1),
                AvatarUrl: reader.IsDBNull(2) ? null : reader.GetString(2)
            );

            return Results.Ok(dto);
        })
        .WithName("GetMe")
        .WithSummary("Get current user profile (read-your-writes safe)");

        group.MapPost("/me/profile", async (
            HttpContext ctx,
            UserProfileDto profile,
            DbRouter router,
            ILastWriteTracker lastWriteTracker) =>
        {
            var userId = GetUserId(ctx);

            await using var conn = router.GetConnectionForWrite();
            await conn.OpenAsync();

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO users (id, email, name, avatar_url, created_at, updated_at)
                VALUES (@id, @email, @name, @avatar, now(), now())
                ON CONFLICT (id) DO UPDATE
                SET email = EXCLUDED.email,
                    name = EXCLUDED.name,
                    avatar_url = EXCLUDED.avatar,
                    updated_at = now();
                """,
                conn))
            {
                cmd.Parameters.AddWithValue("id", userId);
                cmd.Parameters.AddWithValue("email", profile.Email);
                cmd.Parameters.AddWithValue("name", profile.Name);
                cmd.Parameters.AddWithValue("avatar", (object?)profile.AvatarUrl ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }

            await lastWriteTracker.RecordWriteAsync(userId);

            return Results.Ok(new { message = "Profile saved." });
        })
        .WithName("UpdateProfile")
        .WithSummary("Create/update current user profile on leader and record last_write marker");


        group.MapGet("/products", async (DbRouter router) =>
        {
            await using var conn = await router.GetConnectionForReadAsync(requiresReadYourWrites: false);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT id, name, price FROM products ORDER BY name;", conn);

            await using var reader = await cmd.ExecuteReaderAsync();

            var list = new List<ProductDto>();
            while (await reader.ReadAsync())
            {
                list.Add(new ProductDto(
                    Id: reader.GetInt32(0),
                    Name: reader.GetString(1),
                    Price: reader.GetDecimal(2)
                ));
            }

            return Results.Ok(list);
        })
        .WithName("GetProducts")
        .WithSummary("Get product catalog (read-only, can be slightly stale)");

        return routes;
    }

    private static Guid GetUserId(HttpContext ctx)
    {
        var idClaim = ctx.User.FindFirst("sub") ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier);
        if (idClaim == null || !Guid.TryParse(idClaim.Value, out var id))
            throw new InvalidOperationException("No user id available");
        return id;
    }
}
