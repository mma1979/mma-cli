using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

using Mma.Cli.Shared.Data;
using Mma.Cli.UI.Data;

using Radzen;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor().AddHubOptions(o =>
{
    o.MaximumReceiveMessageSize = 10 * 1024 * 1024;
});
builder.Services.AddScoped<DialogService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<TooltipService>();
builder.Services.AddScoped<ContextMenuService>();

builder.Services.AddDbContext<CliDbContext>(options =>
               options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddCors();

builder.Services.AddScoped<CliData>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseCors(opt => opt
.AllowAnyOrigin()
.AllowAnyMethod()
.AllowAnyHeader());


app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// app.MapRazorComponents<Mma.Cli.UI.App>()
//    .AddInteractiveServerRenderMode()
//    .AddAdditionalAssemblies(typeof(Mma.Cli.UI.App).Assembly);

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
