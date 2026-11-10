using SecureProxy.Application.Services;
using SecureProxy.Infrastructure;
using SecureProxy.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddInfrastructure(builder.Configuration);
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<ProxyService>();


var app = builder.Build();

// First in the pipeline, so it catches exceptions from everything after it.
// Unexpected errors become a clean 500: no stack trace reaches the caller (section 10).
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = 500;
    context.Response.Headers["X-Proxy-Source"] = "proxy";
    await context.Response.WriteAsJsonAsync(new { error = "internal_error", correlationId = context.TraceIdentifier });
}));


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
    await DevelopmentSeeder.SeedAsync(db, CancellationToken.None);
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
