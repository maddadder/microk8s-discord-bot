using System.Text.Json;
using discordBot.Logging;
using discordBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace discordBot;
class Program
{

    static async Task<int> Main(string[] args)
    {

        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        var configuration = configurationBuilder.Build();

        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(11) // Set the desired timeout duration in seconds.
        };
        var BotConfig = new BotConfig();
        configuration.Bind("BotConfig", BotConfig);
            
        // Create and configure the service provider.
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(BotConfig)
            .AddSingleton<AdventureBotReadService>() 
            .AddSingleton(httpClient)
            .AddLogging(builder =>
            {
                builder.AddColorConsoleLogger(configuration =>
                {
                    // Replace warning value from appsettings.json of "Cyan"
                    configuration.LogLevelToColorMap[LogLevel.Warning] = ConsoleColor.DarkCyan;
                    // Replace warning value from appsettings.json of "Red"
                    configuration.LogLevelToColorMap[LogLevel.Error] = ConsoleColor.DarkRed;
                });
            })
            .AddEnyimMemcached()
            .BuildServiceProvider();
        // Create an instance of your Bot class and pass the service provider.
        var bot = new Bot(serviceProvider);
        if(!string.IsNullOrEmpty(BotConfig.BotToken)){
            await bot.RunBotAsync(BotConfig.BotToken);
            return 0;
        }
        else
        {
            return 1;
        }
        // Return an exit code (0 for success, other values for errors)
        
    }
}
