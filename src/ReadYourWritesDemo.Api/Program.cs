using Microsoft.Extensions.Caching.StackExchangeRedis;
using ReadYourWritesDemo.Api.Endpoints;
using ReadYourWritesDemo.Api.Infrastructure;
using ReadYourWritesDemo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Redis cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:Configuration"] ?? "localhost:6379";
});

// Our services
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<ILastWriteTracker, LastWriteTracker>();
builder.Services.AddScoped<DbRouter>();

builder.Services.AddHttpContextAccessor();

// Minimal API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Demo-only middleware to create a fake user based on X-Demo-UserId header
app.UseDemoUser();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapApiEndpoints();

app.Run();
