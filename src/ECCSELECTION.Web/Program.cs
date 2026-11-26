using ECCSELECTION.Web.Data;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllersWithViews();

builder.Services.Configure<GitHubSyncOptions>(
    builder.Configuration.GetSection(GitHubSyncOptions.SectionName));
builder.Services.Configure<LlmEnrichmentOptions>(
    builder.Configuration.GetSection(LlmEnrichmentOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=eccselection.db";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<IEccParserService, EccParserService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IMarkdownExportService, MarkdownExportService>();
builder.Services.AddScoped<IEccCatalogService, EccCatalogService>();

builder.Services.AddHttpClient<IRequirementAnalyzerService, RequirementAnalyzerService>();
builder.Services.AddHttpClient<IGeminiCurationService, GeminiCurationService>();
builder.Services.AddHttpClient<ILlmEnrichmentService, LlmEnrichmentService>();
builder.Services.AddHttpClient<IGitHubSyncService, GitHubSyncService>();
builder.Services.AddHttpClient<IHarnessBundleService, HarnessBundleService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
