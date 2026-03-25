using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SmartSchedulingSystem.Data;
using SmartSchedulingSystem.Models;
using SmartSchedulingSystem.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseOracle(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ISchedulerService, SchedulerService>();
builder.Services.AddScoped<IStudentService, StudentService>();

builder.Services.AddControllersWithViews()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.PropertyNamingPolicy = null);

var app = builder.Build();

// ── Global JSON error handler ─────────────────────────────────
// Walks the full exception chain so the real Oracle error
// (buried inside DbUpdateException) always appears in the browser.
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    ctx.Response.ContentType = "application/json";
    ctx.Response.StatusCode = 500;

    var feature = ctx.Features.Get<IExceptionHandlerFeature>();
    var ex = feature?.Error;

    // Collect every message in the chain: EF wraps Oracle inside DbUpdateException
    var messages = new List<string>();
    var current = ex;
    while (current != null)
    {
        messages.Add(current.GetType().Name + ": " + current.Message);
        current = current.InnerException;
    }

    var fullMessage = string.Join(" --> ", messages);
    await ctx.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(fullMessage));

}));

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();