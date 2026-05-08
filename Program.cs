using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SmartSchedulingSystem.Data;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Oracle EF Core ────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseOracle(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Services ─────────────────────────────────────────────────
builder.Services.AddScoped<ISchedulerService, SchedulerService>();
builder.Services.AddScoped<IStudentService, StudentService>();

// ── Session (stores logged-in student ID) ─────────────────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opt =>
{
    opt.IdleTimeout = TimeSpan.FromHours(8);
    opt.Cookie.HttpOnly = true;
    opt.Cookie.IsEssential = true;
});

// ── MVC ───────────────────────────────────────────────────────
builder.Services.AddControllersWithViews()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.PropertyNamingPolicy = null);

// ─────────────────────────────────────────────────────────────

// ── Gemini API ─────────────────────────────────────────────────
builder.Services.AddScoped<GeminiScheduleRanker>();
// ─────────────────────────────────────────────────────────────

var app = builder.Build();

// ── Global JSON error handler ─────────────────────────────────
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    ctx.Response.ContentType = "application/json";
    ctx.Response.StatusCode = 500;
    var feature = ctx.Features.Get<IExceptionHandlerFeature>();
    var messages = new List<string>();
    var current = feature?.Error;
    while (current != null) { messages.Add(current.GetType().Name + ": " + current.Message); current = current.InnerException; }
    await ctx.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(string.Join(" --> ", messages)));
}));

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseStaticFiles();
app.UseRouting();
app.UseSession();           // must be before MapControllers
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();