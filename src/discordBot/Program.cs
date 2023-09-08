using System.Text.Json;

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
        Console.WriteLine("Hello, World!");
        var config = await LoadConfigAsync();
        if(config != null)
        {
            Bot bot = new Bot();
            await bot.RunBotAsync(config.BotToken);
        }
        // Return an exit code (0 for success, other values for errors)
        return 0;
    }
}
