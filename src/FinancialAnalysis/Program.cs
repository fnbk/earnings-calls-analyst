using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FinancialAnalysis
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                Log.Information("Starting Financial Analysis application");

                var startup = new Startup();
                var services = new ServiceCollection();
                startup.ConfigureServices(services);
                
                var serviceProvider = services.BuildServiceProvider();
                var app = serviceProvider.GetRequiredService<App>();
                await app.RunAsync();

                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}




