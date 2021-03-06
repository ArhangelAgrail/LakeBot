using Discord;
using Discord.Commands;
using NadekoBot.Common.Attributes;
using NadekoBot.Core.Common;
using NadekoBot.Core.Modules.Gambling.Common;
using NadekoBot.Core.Modules.Gambling.Common.Blackjack;
using NadekoBot.Core.Modules.Gambling.Services;
using NadekoBot.Core.Services;
using NadekoBot.Extensions;
using System.Linq;
using System.Threading.Tasks;

namespace NadekoBot.Modules.Gambling
{
    public partial class Gambling
    {
        public class BlackJackCommands : GamblingSubmodule<BlackJackService>
        {
            private readonly ICurrencyService _cs;
            private readonly DbService _db;
            private IUserMessage _msg;

            public enum BjAction
            {
                Hit = int.MinValue,
                Stand,
                Double,
            }

            public BlackJackCommands(ICurrencyService cs, DbService db)
            {
                _cs = cs;
                _db = db;
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task BlackJack(ShmartNumber amount)
            {
                if (!await CheckBetMandatory(amount).ConfigureAwait(false))
                    return;

                var newBj = new Blackjack(_cs, _db);
                Blackjack bj;
                if (newBj == (bj = _service.Games.GetOrAdd(Context.Channel.Id, newBj)))
                {
                    if (!await bj.Join(Context.User, amount).ConfigureAwait(false))
                    {
                        _service.Games.TryRemove(Context.Channel.Id, out _);
                        await ReplyErrorLocalized("not_enough", Bc.BotConfig.CurrencySign).ConfigureAwait(false);
                        return;
                    }
                    bj.StateUpdated += Bj_StateUpdated;
                    bj.GameEnded += Bj_GameEnded;
                    bj.Start();

                    await ReplyConfirmLocalized("bj_created").ConfigureAwait(false);
                }
                else
                {
                    if (await bj.Join(Context.User, amount).ConfigureAwait(false))
                        await ReplyConfirmLocalized("bj_joined").ConfigureAwait(false);
                    else
                    {
                        _log.Info($"{Context.User} can't join a blackjack game as it's in " + bj.State.ToString() + " state already.");
                    }
                }

                await Context.Message.DeleteAsync().ConfigureAwait(false);
            }

            private Task Bj_GameEnded(Blackjack arg)
            {
                _service.Games.TryRemove(Context.Channel.Id, out _);
                return Task.CompletedTask;
            }

            private async Task Bj_StateUpdated(Blackjack bj)
            {
                try
                {
                    if (_msg != null)
                    {
                        var _ = _msg.DeleteAsync();
                    }

                    var c = bj.Dealer.Cards.Select(x => x.GetEmojiString());
                    var dealerIcon = "❔ ";
                    if (bj.State == Blackjack.GameState.Ended)
                    {
                        if (bj.Dealer.GetHandValue() == 21)
                            dealerIcon = "💰 ";
                        else if (bj.Dealer.GetHandValue() > 21)
                            dealerIcon = "💥 ";
                        else
                            dealerIcon = "🏁 ";
                    }

                    var cStr = string.Concat(c.Select(x => x.Substring(0, x.Length - 1) + " "));
                    cStr += "\n" + string.Concat(c.Select(x => x.Last() + " "));
                    var embed = new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("BlackJack")
                        .AddField($"{dealerIcon} Dealer's Hand | Value: {bj.Dealer.GetHandValue()}", cStr);

                    if (bj.CurrentUser != null)
                    {
                        embed.WithFooter($"Player to make a choice: {bj.CurrentUser.DiscordUser.ToString()}");
                    }

                    foreach (var p in bj.Players)
                    {
                        c = p.Cards.Select(x => x.GetEmojiString());
                        cStr = "-\t" + string.Concat(c.Select(x => x.Substring(0, x.Length - 1) + " "));
                        cStr += "\n-\t" + string.Concat(c.Select(x => x.Last() + " "));
                        var full = $"{p.DiscordUser.ToString().TrimTo(20)} | Bet: {p.Bet} | Value: {p.GetHandValue()}";
                        if (bj.State == Blackjack.GameState.Ended)
                        {
                            if (p.State == User.UserState.Lost)
                            {
                                full = "❌ " + full;
                            }
                            else
                            {
                                full = "✅ " + full;
                            }
                        }
                        else if (p == bj.CurrentUser)
                            full = "▶ " + full;
                        else if (p.State == User.UserState.Stand)
                            full = "⏹ " + full;
                        else if (p.State == User.UserState.Bust)
                            full = "💥 " + full;
                        else if (p.State == User.UserState.Blackjack)
                            full = "💰 " + full;
                        embed.AddField(full, cStr);
                    }
                    _msg = await Context.Channel.EmbedAsync(embed).ConfigureAwait(false);
                }
                catch
                {

                }
            }

            private string UserToString(User x)
            {
                var playerName = x.State == User.UserState.Bust ?
                    Format.Strikethrough(x.DiscordUser.ToString().TrimTo(30)) :
                    x.DiscordUser.ToString();

                var hand = $"{string.Concat(x.Cards.Select(y => "〖" + y.GetEmojiString() + "〗"))}";


                return $"{playerName} | Bet: {x.Bet}\n";
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public Task Hit() => InternalBlackJack(BjAction.Hit);

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public Task Stand() => InternalBlackJack(BjAction.Stand);

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public Task Double() => InternalBlackJack(BjAction.Double);

            public async Task InternalBlackJack(BjAction a)
            {
                if (!_service.Games.TryGetValue(Context.Channel.Id, out var bj))
                    return;

                if (a == BjAction.Hit)
                    await bj.Hit(Context.User).ConfigureAwait(false);
                else if (a == BjAction.Stand)
                    await bj.Stand(Context.User).ConfigureAwait(false);
                else if (a == BjAction.Double)
                {
                    if (!await bj.Double(Context.User).ConfigureAwait(false))
                    {
                        await ReplyErrorLocalized("not_enough", Bc.BotConfig.CurrencySign).ConfigureAwait(false);
                    }
                }

                await Context.Message.DeleteAsync().ConfigureAwait(false);
            }
        }
    }
}
