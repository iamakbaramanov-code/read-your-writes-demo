using System.Security.Claims;

namespace ReadYourWritesDemo.Api.Infrastructure;

public class DemoUserMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Guid DemoUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public DemoUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            // Try to get user id from header (for demo)
            var userIdHeader = context.Request.Headers["X-Demo-UserId"].FirstOrDefault();
            Guid userId;
            if (!string.IsNullOrWhiteSpace(userIdHeader) && Guid.TryParse(userIdHeader, out var parsed))
            {
                userId = parsed;
            }
            else
            {
                userId = DemoUserId;
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            };
            var identity = new ClaimsIdentity(claims, "Demo");
            var principal = new ClaimsPrincipal(identity);

            context.User = principal;
        }

        await _next(context);
    }
}

public static class DemoUserMiddlewareExtensions
{
    public static IApplicationBuilder UseDemoUser(this IApplicationBuilder app)
        => app.UseMiddleware<DemoUserMiddleware>();
}
