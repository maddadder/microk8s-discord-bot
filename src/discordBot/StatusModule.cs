using Discord.Commands;
using System.Threading.Tasks;

public class StatusModule : ModuleBase<SocketCommandContext>
{
    [Command("status")]
    public async Task StatusAsync()
    {
        await ReplyAsync("The bot is online and running!");
    }
}
