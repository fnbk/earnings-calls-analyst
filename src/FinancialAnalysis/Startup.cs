using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Serilog;
using FinancialAnalysis.Configuration;

namespace FinancialAnalysis
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup()
        {
            // Create configuration
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Initialize logger
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(_configuration)
                .WriteTo.Console()
                .CreateLogger();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Configuration
            services.AddSingleton<IConfiguration>(_configuration);
            services.AddSingleton<AppSettings>(AppSettings.LoadSettings());

            // Logging
            services.AddLogging(loggingBuilder => 
                loggingBuilder.AddSerilog(dispose: true));

            // HTTP
            services.AddHttpClient();

            // Application Services
            services.AddTransient<App>();
        }
    }
} 