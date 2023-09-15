using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using discordBot.Services;
using Enyim.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Read;

namespace discordBot;

public class Bot
{
    private DiscordSocketClient _client;
    private CommandService _commands;
    private IServiceProvider _services;
    private readonly AdventureBotReadService _adventureBotReadService;
    private readonly IMemcachedClient _memcachedClient;
    private readonly ILogger _logger;
    public Bot(IServiceProvider services)
    {
        _services = services;
        _adventureBotReadService = services.GetRequiredService<AdventureBotReadService>();
        _memcachedClient = services.GetRequiredService<IMemcachedClient>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<Bot>();
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
        _logger.LogInformation($"Logged in as {_client.CurrentUser.Username}");
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
            _logger.LogInformation(json);
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
        _logger.LogInformation($"Received message of type: {command.GetType().Name}");
        // Check the name of the command
        if (command.CommandName == "status")
        {
            // Handle the "status" command
            var priorInstanceId = await _memcachedClient.GetValueOrCreateAsync(
                "instanceId",
                int.MaxValue,
                async () => command.Data.Options?.FirstOrDefault(o => o.Name == "instanceid")?.Value?.ToString() ?? string.Empty);
            var instanceId = command.Data.Options?.FirstOrDefault(o => o.Name == "instanceid")?.Value?.ToString();
            if (!string.IsNullOrEmpty(instanceId) || !string.IsNullOrEmpty(priorInstanceId))
            {
                if(!string.IsNullOrEmpty(instanceId))
                {
                    //instanceId wins
                    await _memcachedClient.SetAsync("instanceId", instanceId, int.MaxValue);
                }
                else
                {
                    instanceId = priorInstanceId;
                }
                var currentVote = "";
                try
                {
                    if(votingTimer != null)
                    {
                        _logger.LogInformation($"A vote is in progress. The game will advance once the tally is complete.");
                        await command.RespondAsync("A vote is in progress. The game will advance once the tally is complete.");
                        return;
                    }
                    _logger.LogInformation($"Calling long running DiscordLoopGetAsync with instanceId '{instanceId}'");
                    await command.RespondAsync("Getting Status...");
                    var response = "Could not get status. An invalid instanceId was provided or the request timed out.";
                    var votingCounter = await _adventureBotReadService.DiscordLoopGetAsync(instanceId);
                    await _memcachedClient.SetAsync("votingCounter", votingCounter, int.MaxValue);
                    if(votingCounter.VoterList.Any())
                    {
                        currentVote = votingCounter.VoterList.First().Value;
                    }
                    if(!string.IsNullOrEmpty(votingCounter.PriorVote))
                    {
                        _logger.LogInformation($"Please vote by tapping on one of the following reactions to advance the game");
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
                        _logger.LogInformation($"The game is over. Restarting...");
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
                        await _adventureBotReadService.DiscordLoopPutAsync(instanceId, input);
                        await _memcachedClient.RemoveAsync("voteCounts");
                        await _memcachedClient.RemoveAsync("totalVotes");
                        await _memcachedClient.RemoveAsync("priorMessageId");
                        _logger.LogInformation($"Game started over from begining.");
                    }
                }
                catch(Exception e)
                {
                    _logger.LogError(e.Message);
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
                _logger.LogWarning("You must provide the instanceId in order to use this command.");
                await command.RespondAsync("You must provide the instanceId in order to use this command.");
            }
        }
    }

    private int requiredVotes = 1; // Adjust the required number of votes as needed
    private Timer votingTimer = null;

    private async Task HandleReactionAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        var user = _client.GetUser(reaction.UserId);
        if (user == null) {
            _logger.LogWarning($"HandleReactionAsync, user == null");
        }
        else if (user.IsBot) return;

        var priorMessageObj = await _memcachedClient.GetAsync<ulong>("priorMessageId");
        if(!priorMessageObj.HasValue)
        {
            _logger.LogWarning("We lost the priorMessageId");
            return;
        }
        Dictionary<string, int> voteCounts = await _memcachedClient.GetValueOrCreateAsync(
                    "voteCounts",
                    int.MaxValue,
                    async () => new Dictionary<string, int>());
        _logger.LogInformation("Check if the reaction is added to the correct message");
        var priorMessageId = priorMessageObj.Value;
        if (message.Id == priorMessageId)
        {
            var emojiName = reaction.Emote.Name;
            _logger.LogInformation($"received a {emojiName} vote");
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
                _logger.LogInformation($"totalVotes: {totalVotes}");
                if (totalVotes == 1 || votingTimer == null)
                {
                    _logger.LogInformation("Start the timer when the first vote is cast");
                    StartVotingTimer();
                }
            }
        }
    }

    private void StartVotingTimer()
    {
        _logger.LogInformation($"Start a timer to check if the required votes are reached after a certain period");
        votingTimer = new Timer(_ =>
        {
            _logger.LogInformation($"Check the votes and proceed asynchronously");
            Task.Run(async () =>
            {
                await CheckVotesAndProceed();
            }).Wait(); // Wait for the async task to complete within the timer callback
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(-1)); // Adjust the timer duration as needed
    }

    private async Task CheckVotesAndProceed()
    {
        _logger.LogInformation($"Check if the required number of votes has been reached");
        var instanceIdObj = await _memcachedClient.GetAsync<string>("instanceId");
        if(!instanceIdObj.HasValue)
        {
            _logger.LogWarning("We lost the instanceId");
            return;
        }
        var instanceId = instanceIdObj.Value;
        var voteCountsObj = await _memcachedClient.GetAsync<Dictionary<string, int>>("voteCounts");
        if(!voteCountsObj.HasValue)
        {
            _logger.LogWarning("We lost the voteCounts");
            return;
        }
        var voteCounts = voteCountsObj.Value;
        var totalVotesObj = await _memcachedClient.GetAsync<int>("totalVotes");
        if(!totalVotesObj.HasValue)
        {
            _logger.LogWarning("We lost the totalVotes");
            return;
        }
        var totalVotes = totalVotesObj.Value;
        if (totalVotes >= requiredVotes)
        {
            _logger.LogInformation($"Determine the winning options based on the vote counts");
            var maxVoteCount = voteCounts.Values.Max();
            var winningOptions = voteCounts.Where(kv => kv.Value == maxVoteCount).Select(kv => kv.Key).ToList();
            _logger.LogInformation($"Check if there's a tie, maxVoteCount={maxVoteCount}, winningOptionsCount={winningOptions.Count}");
            if (winningOptions.Count == 1)
            {
                var winningOption = winningOptions.First();
                var index = optionEmojis.IndexOf(winningOption);
                var votingCounter = await _memcachedClient.GetValueOrCreateAsync(
                "votingCounter",
                int.MaxValue,
                async () => await _adventureBotReadService.DiscordLoopGetAsync(instanceId));
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
                    await _adventureBotReadService.DiscordLoopPutAsync(instanceId, input);
                    _logger.LogInformation($"Voting is complete.");
                    await _memcachedClient.RemoveAsync("voteCounts");
                    await _memcachedClient.RemoveAsync("totalVotes");
                    _logger.LogInformation($"set priorMessageId = 0 to stop incoming votes until /status is called");
                    await _memcachedClient.RemoveAsync("priorMessageId");
                    _logger.LogInformation($"set votingTimer = null to allow /status to start another vote");
                    votingTimer = null;
                }
            }
            else
            {
                _logger.LogInformation($"Handle a tie by resetting the votes and allowing users to vote again");
                await _memcachedClient.RemoveAsync("voteCounts");
                await _memcachedClient.RemoveAsync("totalVotes");
                StartVotingTimer();
            }
        }
        else
        {
            _logger.LogInformation($"Reset the vote counts and total votes if the timer expires without reaching the required votes");
            await _memcachedClient.RemoveAsync("voteCounts");
            await _memcachedClient.RemoveAsync("totalVotes");
        }
    }

    private async Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        var user = _client.GetUser(reaction.UserId);
        if (user == null) {
            _logger.LogWarning($"HandleReactionRemovedAsync, user == null");
        }
        else if (user.IsBot) return;

        if(!(await _memcachedClient.GetAsync<ulong>("priorMessageId")).HasValue)
        {
            _logger.LogWarning("We lost the priorMessageId");
            return;
        }
        var voteCountsObj = await _memcachedClient.GetAsync<Dictionary<string, int>>("voteCounts");
        if(!voteCountsObj.HasValue)
        {
            _logger.LogWarning("We lost the voteCounts");
            return;
        }
        var voteCounts = voteCountsObj.Value;
        var totalVotesObj = await _memcachedClient.GetAsync<int>("totalVotes");
        if(!totalVotesObj.HasValue)
        {
            _logger.LogWarning("We lost the totalVotes");
            return;
        }
        var totalVotes = totalVotesObj.Value;

        _logger.LogInformation("Check if the reaction is added to the correct message");
        var priorMessageIdObj = await _memcachedClient.GetAsync<ulong>("priorMessageId");
        if(!priorMessageIdObj.HasValue)
        {
            _logger.LogWarning("We lost the priorMessageIdObj");
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
                _logger.LogInformation($"totalVotes: {totalVotes}");
            }
        }
    }

}
