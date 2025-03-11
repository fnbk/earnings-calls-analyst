using Microsoft.Extensions.Configuration;
using System.Text.Json.Serialization;
using System.Configuration;

namespace FinancialAnalysis.Configuration
{
    public class AppSettings
    {
        [ConfigurationKeyName("AZURE_OAI_ENDPOINT")]
        public string AzureOaiEndpoint { get; set; }

        [ConfigurationKeyName("AZURE_OAI_KEY")]
        public string AzureOaiKey { get; set; }

        [ConfigurationKeyName("AZURE_OAI_DEPLOYMENT")]
        public string AzureOaiDeployment { get; set; }

        [ConfigurationKeyName("FINANCIAL_MODELING_PREP_KEY")]
        public string FinancialModelingPrepKey { get; set; }

        [ConfigurationKeyName("PROGRAM_VERSION")]
        public string ProgramVersion { get; set; } = "1.0.0";

        [ConfigurationKeyName("START_DATE")]
        public DateTime StartDate { get; set; }

        [ConfigurationKeyName("END_DATE")]
        public DateTime EndDate { get; set; }

        [ConfigurationKeyName("NUMBER_OF_QUARTERS_FOR_EPS_HISTORY")]
        public int NumberOfQuartersForEpsHistory { get; set; } = 8;

        [ConfigurationKeyName("USE_CACHE")]
        public bool UseCache { get; set; } = true;

        [ConfigurationKeyName("ONLY_LATEST")]
        public bool OnlyLatest { get; set; } = false;

        public static AppSettings LoadSettings()
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            Console.WriteLine($"Loading configuration from: {Directory.GetCurrentDirectory()}");
            Console.WriteLine($"Environment: {environment}");

            return new AppSettings
            {
                AzureOaiEndpoint = configuration["AZURE_OAI_ENDPOINT"],
                AzureOaiKey = configuration["AZURE_OAI_KEY"],
                AzureOaiDeployment = configuration["AZURE_OAI_DEPLOYMENT"],
                FinancialModelingPrepKey = configuration["FINANCIAL_MODELING_PREP_KEY"],
                ProgramVersion = configuration["PROGRAM_VERSION"],
                StartDate = configuration.GetValue<DateTime>("START_DATE"),
                EndDate = configuration.GetValue<DateTime>("END_DATE"),
                NumberOfQuartersForEpsHistory = configuration.GetValue<int>("NUMBER_OF_QUARTERS_FOR_EPS_HISTORY"),
                UseCache = configuration.GetValue<bool>("USE_CACHE"),
                OnlyLatest = configuration.GetValue<bool>("ONLY_LATEST")
            };
        }
    }
} 