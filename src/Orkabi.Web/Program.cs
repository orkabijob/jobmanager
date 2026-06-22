using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

var dbProvider = builder.Configuration["Database:Provider"] ?? "Npgsql";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    var cs = builder.Configuration.GetConnectionString("Default");
    if (dbProvider == "Sqlite")
        o.UseSqlite(cs);          // inner-loop tests; schema built via EnsureCreated() in the factory
    else
        o.UseNpgsql(cs);          // prod: Neon pooled endpoint
    // NOTE (Task 4): the audit interceptor is added here once it exists — keep this single
    // registration as the one place the provider AND interceptors are wired.
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

if (!app.Environment.IsEnvironment("Testing") && dbProvider != "Sqlite")
{
    var migrateCs = app.Configuration.GetConnectionString("Migrations")
                    ?? app.Configuration.GetConnectionString("Default");
    var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(migrateCs).Options;
    await using var migrateDb = new AppDbContext(opts);
    await migrateDb.Database.MigrateAsync();
}

app.Run();

public partial class Program { }
