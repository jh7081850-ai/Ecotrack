var builder = WebApplication.CreateBuilder(args);

// 1. Servicios MVC
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

// 2. SERVICIOS DE SESIÓN EN MEMORIA (OBLIGATORIO)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(4);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// 3. Pipeline HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();

app.UseRouting();

// 4. ACTIVAR SESIÓN (DEBE IR ANTES DE UseAuthorization)
app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Usuarios}/{action=Index}/{id?}");

app.Run();
