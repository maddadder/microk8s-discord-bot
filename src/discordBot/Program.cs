using System.Text.Json;
using discordBot.Services;
using Microsoft.Extensions.DependencyInjection;

namespace discordBot;
class Program
{
    public static async Task<BotConfig> LoadConfigAsync()
    {
        using (var streamReader = new StreamReader("config.json"))
        {
            var json = await streamReader.ReadToEndAsync();
            var config = JsonSerializer.Deserialize<BotConfig>(json);
            return config;
        }
    }

    static async Task<int> Main(string[] args)
    {
        var config = await LoadConfigAsync();
        if(config != null)
        {
            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(11) // Set the desired timeout duration in seconds.
            };
            
            // Create and configure the service provider.
            var serviceProvider = new ServiceCollection()
                .AddSingleton<AdventureBotReadService>() 
                .AddSingleton(httpClient)
                .BuildServiceProvider();

            // Create an instance of your Bot class and pass the service provider.
            var bot = new Bot(serviceProvider);
            await bot.RunBotAsync(config.BotToken);
        }
        // Return an exit code (0 for success, other values for errors)
        return 0;
    }
}
