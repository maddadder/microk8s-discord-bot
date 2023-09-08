using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Reflection;
using System.Threading.Tasks;

public class Bot
{
    private DiscordSocketClient _client;
    private CommandService _commands;
    private IServiceProvider _services;

    public async Task RunBotAsync(string BotToken)
    {
        _client = new DiscordSocketClient();
        _commands = new CommandService();
        // Initialize other services if needed.

        await RegisterCommandsAsync();

        await _client.LoginAsync(TokenType.Bot, BotToken);
        await _client.StartAsync();

        // Listen for messages and handle commands.
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += HandleCommandAsync;

        await Task.Delay(-1);
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log);
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        Console.WriteLine($"Logged in as {_client.CurrentUser.Username}");
        return Task.CompletedTask;
    }

    private async Task RegisterCommandsAsync()
    {
        // Register your commands here.
        // For example, you can use the [Command] attribute to mark methods as commands.
        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
    }

    private async Task HandleCommandAsync(SocketMessage arg)
    {
        if (arg is SocketUserMessage message)
        {
            Console.WriteLine($"Received message of type: {message.GetType().Name}");
            
            var context = new SocketCommandContext(_client, message);

            if (message.Author.IsBot)
                return;

            int argPos = 0;
            if (message.HasStringPrefix("!", ref argPos)) // Change the prefix as needed.
            {
                var result = await _commands.ExecuteAsync(context, argPos, _services);
                if (!result.IsSuccess)
                    Console.WriteLine(result.ErrorReason);
            }
        }
        else
        {
            Console.WriteLine($"Received message of unknown type: {arg.GetType().Name}");
        }
    }
}
