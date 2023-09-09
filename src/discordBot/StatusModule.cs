using Discord.Commands;
using discordBot.Services;
using System.Threading.Tasks;

public class StatusModule : ModuleBase<SocketCommandContext>
{
    private readonly AdventureBotReadService _adventureBotReadService;

    public StatusModule(AdventureBotReadService adventureBotReadService)
    {
        _adventureBotReadService = adventureBotReadService;
    }


    [Command("status")]
    public async Task StatusAsync(string instanceId)
    {
        var currentVote = "";
        try
        {
            Console.WriteLine($"Calling long running DiscordLoopGetAsync with instanceId '{instanceId}'");
            var votingCounter = await _adventureBotReadService.DiscordLoopGetAsync(instanceId);
            if(votingCounter.VoterList.Any())
            {
                currentVote = votingCounter.VoterList.First().Value;
            }
            Console.WriteLine($"ReplyAsinc with the current vote");
            await ReplyAsync($"The prior vote was '{votingCounter.PriorVote}'. The current vote is '{currentVote}'");
        }
        catch(Exception e)
        {
            Console.WriteLine("DiscordLoopGetAsync failed with the following error:");
            Console.WriteLine(e.Message);
            await ReplyAsync("Could not get the status. Please try again.");
        }
    }
}
