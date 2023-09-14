using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using discordBot.Services;
using Enyim.Caching;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Read;

namespace discordBot;

public class Bot
{
    private DiscordSocketClient _client;
    private CommandService _commands;
    private IServiceProvider _services;
    private readonly AdventureBotReadService _adventureBotReadService;
    private DiscordVotingCounter votingCounter = null;
    private readonly IMemcachedClient _memcachedClient;
    public Bot(IServiceProvider services)
    {
        _services = services;
        _adventureBotReadService = services.GetRequiredService<AdventureBotReadService>();
        _memcachedClient = services.GetRequiredService<IMemcachedClient>();
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
        _client.ReactionAdded += HandleReactionAsync;
        _client.ReactionRemoved += HandleReactionRemovedAsync;
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
    List<string> optionEmojis = new List<string>
    {
        "1️⃣", 
        "2️⃣", 
        "3️⃣", 
        "4️⃣", 
        "5️⃣", 
        "6️⃣",
        "7️⃣",
        "8️⃣",
        "9️⃣",
        "🔟"
    };
    private async Task SlashCommandHandler(SocketSlashCommand command)
    {
        Console.WriteLine($"Received message of type: {command.GetType().Name}");
        // Check the name of the command
        if (command.CommandName == "status")
        {
            // Handle the "status" command
            var priorInstanceId = await _memcachedClient.GetValueOrCreateAsync(
                "instanceId",
                int.MaxValue,
                async () => command.Data.Options?.FirstOrDefault(o => o.Name == "instanceid")?.Value?.ToString() ?? string.Empty);
            var instanceid = command.Data.Options?.FirstOrDefault(o => o.Name == "instanceid")?.Value?.ToString();
            if (!string.IsNullOrEmpty(instanceid) || !string.IsNullOrEmpty(priorInstanceId))
            {
                if(!string.IsNullOrEmpty(instanceid))
                {
                    //instanceid wins
                    await _memcachedClient.SetAsync("instanceId", instanceid, int.MaxValue);
                }
                else
                {
                    instanceid = priorInstanceId;
                }
                var currentVote = "";
                try
                {
                    if(votingTimer != null)
                    {
                        Console.WriteLine($"A vote is in progress. The game will advance once the tally is complete.");
                        await command.RespondAsync("A vote is in progress. The game will advance once the tally is complete.");
                        return;
                    }
                    Console.WriteLine($"Calling long running DiscordLoopGetAsync with instanceid '{instanceid}'");
                    await command.RespondAsync("Getting Status...");
                    var response = "Could not get status. An invalid instanceid was provided or the request timed out.";
                    votingCounter = await _adventureBotReadService.DiscordLoopGetAsync(instanceid);
                    if(votingCounter.VoterList.Any())
                    {
                        currentVote = votingCounter.VoterList.First().Value;
                    }
                    if(!string.IsNullOrEmpty(votingCounter.PriorVote))
                    {
                        Console.WriteLine($"Please vote by tapping on one of the following reactions to advance the game");
                        response = $"Please vote by tapping on one of the following reactions to advance the game";
                    }
                    var initialResponse = await command.GetOriginalResponseAsync(); // Get the initial response message
                    var priorMessageId = initialResponse.Id;
                    await _memcachedClient.SetAsync("priorMessageId", priorMessageId, int.MaxValue);
                    if(votingCounter.GameOptions.Any())
                    {
                        await initialResponse.ModifyAsync(properties =>
                        {
                            properties.Content = response;
                        });
                        foreach (var (index, option) in votingCounter.GameOptions.Select((value, index) => (index, value)))
                        {
                            if (index < optionEmojis.Count)
                            {
                                var emoji = optionEmojis[index];
                                await initialResponse.AddReactionAsync(new Emoji(emoji));
                            }
                        }
                        StartVotingTimer();
                    }
                    else
                    {
                        Console.WriteLine($"The game is over. Restarting...");
                        await initialResponse.ModifyAsync(properties =>
                        {
                            properties.Content = "The game is over. Restarting...";
                        });
                        DiscordLoopInput input = new DiscordLoopInput()
                        {
                            GameState = "begin",
                            SubscriberId = votingCounter.VoteInstanceId,
                            TargetChannelId = votingCounter.TargetChannelId
                        };
                        await _adventureBotReadService.DiscordLoopPutAsync(instanceid, input);
                        await _memcachedClient.RemoveAsync("voteCounts");
                        await _memcachedClient.RemoveAsync("totalVotes");
                        await _memcachedClient.RemoveAsync("priorMessageId");
                        Console.WriteLine($"Game started over from begining.");
                    }
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
                    await _memcachedClient.RemoveAsync("instanceId");
                }
            }
            else
            {
                await command.RespondAsync("You must provide the instanceid in order to use this command.");
            }
        }
    }

    private int requiredVotes = 1; // Adjust the required number of votes as needed
    private Timer votingTimer = null;

    private async Task HandleReactionAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (_client.GetUser(reaction.UserId).IsBot) return;

        var priorMessageObj = await _memcachedClient.GetAsync<ulong>("priorMessageId");
        if(!priorMessageObj.HasValue)
        {
            Console.WriteLine("We lost the priorMessageId");
            return;
        }
        Dictionary<string, int> voteCounts = await _memcachedClient.GetValueOrCreateAsync(
                    "voteCounts",
                    int.MaxValue,
                    async () => new Dictionary<string, int>());
        Console.WriteLine("Check if the reaction is added to the correct message");
        var priorMessageId = priorMessageObj.Value;
        if (message.Id == priorMessageId)
        {
            var emojiName = reaction.Emote.Name;
            Console.WriteLine($"received a {emojiName} vote");
            // Check if the emoji is one of the valid options
            if (optionEmojis.Contains(emojiName))
            {
                // Update the vote count for the emoji
                if (!voteCounts.ContainsKey(emojiName))
                {
                    voteCounts[emojiName] = 1;
                }
                else
                {
                    voteCounts[emojiName]++;
                }
                await _memcachedClient.SetAsync("voteCounts", voteCounts, int.MaxValue);
                var totalVotes = await _memcachedClient.GetValueOrCreateAsync(
                    "totalVotes",
                    int.MaxValue,
                    async () => 0);

                totalVotes++;
                await _memcachedClient.SetAsync("totalVotes", totalVotes, int.MaxValue);
                Console.WriteLine($"totalVotes: {totalVotes}");
                if (totalVotes == 1)
                {
                    Console.WriteLine("Start the timer when the first vote is cast");
                    StartVotingTimer();
                }
            }
        }
    }

    private void StartVotingTimer()
    {
        Console.WriteLine($"Start a timer to check if the required votes are reached after a certain period");
        votingTimer = new Timer(_ =>
        {
            Console.WriteLine($"Check the votes and proceed asynchronously");
            Task.Run(async () =>
            {
                await CheckVotesAndProceed();
            }).Wait(); // Wait for the async task to complete within the timer callback
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(-1)); // Adjust the timer duration as needed
    }

    private async Task CheckVotesAndProceed()
    {
        Console.WriteLine($"Check if the required number of votes has been reached");
        var voteCountsObj = await _memcachedClient.GetAsync<Dictionary<string, int>>("voteCounts");
        if(!voteCountsObj.HasValue)
        {
            Console.WriteLine("We lost the voteCounts");
            return;
        }
        var voteCounts = voteCountsObj.Value;
        var totalVotesObj = await _memcachedClient.GetAsync<int>("totalVotes");
        if(!totalVotesObj.HasValue)
        {
            Console.WriteLine("We lost the totalVotes");
            return;
        }
        var totalVotes = totalVotesObj.Value;
        if (totalVotes >= requiredVotes)
        {
            Console.WriteLine($"Determine the winning options based on the vote counts");
            var maxVoteCount = voteCounts.Values.Max();
            var winningOptions = voteCounts.Where(kv => kv.Value == maxVoteCount).Select(kv => kv.Key).ToList();
            Console.WriteLine($"Check if there's a tie, maxVoteCount={maxVoteCount}, winningOptionsCount={winningOptions.Count}");
            if (winningOptions.Count == 1)
            {
                var winningOption = winningOptions.First();
                var index = optionEmojis.IndexOf(winningOption);
                var gameOptionsList = votingCounter.GameOptions.ToList();

                if (index < gameOptionsList.Count)
                {
                    var option = gameOptionsList[index];
                    DiscordLoopInput input = new DiscordLoopInput()
                    {
                        GameState = option.Next,
                        SubscriberId = votingCounter.VoteInstanceId,
                        TargetChannelId = votingCounter.TargetChannelId
                    };
                    var instanceId = await _memcachedClient.GetValueAsync<string>("instanceId");
                    if(!string.IsNullOrEmpty(instanceId))
                    {
                        await _adventureBotReadService.DiscordLoopPutAsync(instanceId, input);
                        Console.WriteLine($"Voting is complete.");
                        await _memcachedClient.RemoveAsync("voteCounts");
                        await _memcachedClient.RemoveAsync("totalVotes");
                        Console.WriteLine($"set priorMessageId = 0 to stop incoming votes until /status is called");
                        await _memcachedClient.RemoveAsync("priorMessageId");
                        Console.WriteLine($"set votingTimer = null to allow /status to start another vote");
                        votingTimer = null;
                    }
                }
            }
            else
            {
                Console.WriteLine($"Handle a tie by resetting the votes and allowing users to vote again");
                await _memcachedClient.RemoveAsync("voteCounts");
                await _memcachedClient.RemoveAsync("totalVotes");
                StartVotingTimer();
            }
        }
        else
        {
            Console.WriteLine($"Reset the vote counts and total votes if the timer expires without reaching the required votes");
            await _memcachedClient.RemoveAsync("voteCounts");
            await _memcachedClient.RemoveAsync("totalVotes");
        }
    }

    private async Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (_client.GetUser(reaction.UserId).IsBot) return;

        if(!(await _memcachedClient.GetAsync<ulong>("priorMessageId")).HasValue)
        {
            Console.WriteLine("We lost the priorMessageId");
            return;
        }
        var voteCountsObj = await _memcachedClient.GetAsync<Dictionary<string, int>>("voteCounts");
        if(!voteCountsObj.HasValue)
        {
            Console.WriteLine("We lost the voteCounts");
            return;
        }
        var voteCounts = voteCountsObj.Value;
        var totalVotesObj = await _memcachedClient.GetAsync<int>("totalVotes");
        if(!totalVotesObj.HasValue)
        {
            Console.WriteLine("We lost the totalVotes");
            return;
        }
        var totalVotes = totalVotesObj.Value;

        Console.WriteLine("Check if the reaction is added to the correct message");
        var priorMessageIdObj = await _memcachedClient.GetAsync<ulong>("priorMessageId");
        if(!priorMessageIdObj.HasValue)
        {
            Console.WriteLine("We lost the priorMessageIdObj");
            return;
        }
        var priorMessageId = priorMessageIdObj.Value;
        if (message.Id == priorMessageId)
        {
            var emojiName = reaction.Emote.Name;

            if (optionEmojis.Contains(emojiName) && voteCounts.ContainsKey(emojiName) && voteCounts[emojiName] > 0)
            {
                voteCounts[emojiName]--;
                await _memcachedClient.SetAsync("voteCounts", voteCounts, int.MaxValue);
                totalVotes--;
                await _memcachedClient.SetAsync("totalVotes", totalVotes, int.MaxValue);
                Console.WriteLine($"totalVotes: {totalVotes}");
            }
        }
    }

}
