using simple_bloomberg_terminal.Repositories;

// ── Web aplikacija ────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

// AddSingleton = one instance for the full app lifetime (Spring's default singleton scope).
// Order matters: CompanyMockRepository depends on ICountryRepository,
// EventMockRepository depends on both — ASP.NET Core DI resolves the graph automatically.
builder.Services.AddSingleton<ICountryRepository, CountryMockRepository>();
builder.Services.AddSingleton<ICompanyRepository, CompanyMockRepository>();
builder.Services.AddSingleton<IEventRepository, EventMockRepository>();
builder.Services.AddSingleton<ICountryDetailsRepository, CountryDetailsMockRepository>();

var app = builder.Build();

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
