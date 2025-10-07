using ABCRetails.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ABCRetails
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            // Configure HttpClient for Functions API
            // This is the correct way to register a typed client and its configuration.
            builder.Services.AddHttpClient<IFunctionsApiService, FunctionsApiService>(client =>
            {
                var functionsBaseUrl = builder.Configuration["FunctionsBaseUrl"];
                if (string.IsNullOrEmpty(functionsBaseUrl))
                {
                    throw new InvalidOperationException("FunctionsBaseUrl is not configured in appsettings.json.");
                }
                client.BaseAddress = new Uri(functionsBaseUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            // The line below was redundant and is now removed.
            // builder.Services.AddScoped<IFunctionsApiService, FunctionsApiService>(); 

            builder.Services.AddLogging();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}