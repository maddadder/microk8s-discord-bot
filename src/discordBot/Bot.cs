using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using discordBot.Services;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace discordBot;

public class Bot
{
    private DiscordSocketClient _client;
    private CommandService _commands;
    private IServiceProvider _services;
    private readonly AdventureBotReadService _adventureBotReadService;
    private string priorInstanceId = string.Empty;
    public Bot(IServiceProvider services)
    {
        _services = services;
        _adventureBotReadService = services.GetRequiredService<AdventureBotReadService>();
    }

    public async Task RunBotAsync(string BotToken)
    {
        _client = new DiscordSocketClient();
        _commands = new CommandService();
        // Initialize other services if needed.

        await _client.LoginAsync(TokenType.Bot, BotToken);
        await _client.StartAsync();

        // Listen for messages and handle commands.
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.SlashCommandExecuted += SlashCommandHandler;

        
        await Task.Delay(-1);
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log);
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        Console.WriteLine($"Logged in as {_client.CurrentUser.Username}");
        // Let's do our global command
        var globalCommand = new SlashCommandBuilder();
        globalCommand.WithName("status");
        globalCommand.WithDescription("Get the status of the bot");
        globalCommand.AddOption("instanceid", ApplicationCommandOptionType.String, "The instanceid of the game", isRequired: false);
        try
        {
            // With global commands we don't need the guild.
            await _client.CreateGlobalApplicationCommandAsync(globalCommand.Build());
            // Using the ready event is a simple implementation for the sake of the example. Suitable for testing and development.
            // For a production bot, it is recommended to only run the CreateGlobalApplicationCommandAsync() once for each command.
        }
        catch(HttpException exception)
        {
            // If our command was invalid, we should catch an ApplicationCommandException. This exception contains the path of the error as well as the error message. You can serialize the Error field in the exception to get a visual of where your error is.
            var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);

            // You can send this error somewhere or just print it to the console, for this example we're just going to print it.
            Console.WriteLine(json);
        }
    }

    private async Task SlashCommandHandler(SocketSlashCommand command)
    {
        Console.WriteLine($"Received message of type: {command.GetType().Name}");
        // Check the name of the command
        if (command.CommandName == "status")
        {
            // Handle the "status" command
            var instanceid = command.Data.Options?.FirstOrDefault(o => o.Name == "instanceid")?.Value?.ToString();
            if (!string.IsNullOrEmpty(instanceid) || !string.IsNullOrEmpty(priorInstanceId))
            {
                if(string.IsNullOrEmpty(instanceid)){
                    instanceid = priorInstanceId;
                }
                var currentVote = "";
                try
                {
                    Console.WriteLine($"Calling long running DiscordLoopGetAsync with instanceid '{instanceid}'");
                    await command.RespondAsync("Getting Status...");
                    var response = "Could not get status. An invalid instanceid was provided or the request timed out.";
                    var votingCounter = await _adventureBotReadService.DiscordLoopGetAsync(instanceid);
                    if(votingCounter.VoterList.Any())
                    {
                        currentVote = votingCounter.VoterList.First().Value;
                    }
                    if(!string.IsNullOrEmpty(votingCounter.PriorVote))
                    {
                        Console.WriteLine($"ReplyAsinc with the current vote");
                        response = $"The prior vote was '{votingCounter.PriorVote}'. The current vote is '{currentVote}'";
                    }
                    var initialResponse = await command.GetOriginalResponseAsync(); // Get the initial response message
                    await initialResponse.ModifyAsync(properties =>
                    {
                        properties.Content = response;
                    });
                    priorInstanceId = instanceid;
                }
                catch(Exception e)
                {
                    Console.WriteLine("DiscordLoopGetAsync failed with the following error:");
                    Console.WriteLine(e.Message);
                    var initialResponse = await command.GetOriginalResponseAsync(); // Get the initial response message
                    await initialResponse.ModifyAsync(properties =>
                    {
                        properties.Content = "Could not get the status. Please try again.";
                    });
                    priorInstanceId = string.Empty;
                }
            }
            else
            {
                await command.RespondAsync("You must provide the instanceid in order to use this command.");
            }
        }
    }
}
