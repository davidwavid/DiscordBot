﻿using Discord;
using Discord.WebSocket;
using DiscordBot.Exceptions;
using DiscordBot.Games;
using DiscordBot.Games.Models;
using Common.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static DiscordBot.Models.CoinAccounts;
using static DiscordBot.Games.Blackjack;

namespace DiscordBot.Managers
{
    public partial class CommandManager
    {
        private async Task GameRoulette(DiscordSocketClient client, SocketMessage message, List<string> args)
        {
            if (args.Count != 1)
                throw new BadSyntaxException();

            ulong userId = message.Author.Id;

            CoinAccount coinAccount = await _coinService.Get(userId, message.Author.Username);

            string[] inputs = args[0].Split(',');
            var inputBets = new List<RouletteBet>();

            int minPercentBetRequired = 10;
            double tenPercentNw = coinAccount.NetWorth * ((double)minPercentBetRequired / 100);
            foreach (var input in inputs)
            {
                //each 'input' looks like e.g. red-1000
                string[] inputParams = input.Split('-');

                if(!TryExtractBetAmount(inputParams, coinAccount, out double betAmount, betAmountIndex: 1))
                    throw new BadSyntaxException();

                inputBets.Add(new RouletteBet(userId, inputParams[0], betAmount));
            }

            double inputMoney = 0;
            inputBets.Where(b => b.RoulleteBetType != BetType.NotValid).ToList().ForEach(b => inputMoney += b.Amount);

            var bet = await _betManager.InitiateBet(userId, message.Author.Username, inputMoney);
            bool wasBonusGranted = bet.WasBonusGranted;
            bool isFirstGameOfTheDay = bet.IsFirstGameOfTheDay;

            string resultString = "";
            List<RouletteBet> winningBets;
            try
            {
                var resultTuple = new Roulette().Play(inputBets);
                for (int i = 0; i < resultTuple.Item1.Count; i++)
                {
                    string value = resultTuple.Item1[i];
                    string str = value == "-1" ? "00" : value;

                    resultString += i == 0 ? $"**{str}**, " : $"{str}, ";
                }

                resultString = resultString.TrimEnd(' ').TrimEnd(',');

                 winningBets = resultTuple.Item2;
            }
            catch (Exception)
            {
                await _betManager.CancelBet(userId, message.Author.Username, inputMoney);
                throw;
            }

            double baseWinnings = 0;
            string output = $"Winning results were {resultString}.\n";
            if (winningBets.Count == 0)
            {
                output += $"{message.Author.Username} you did not make any successful bets. Your net worth is now ${FormatHelper.GetCommaNumber(coinAccount.NetWorth)}.";
                var betResolve = await _betManager.ResolveBet(userId, message.Author.Username, inputMoney, baseWinnings, isFirstGameOfTheDay);
            }
            else
            {
                output += "`Your winning bets were:`\n";
                foreach (var winningBet in winningBets)
                {
                    double amountBack = winningBet.Amount + (winningBet.PayoutMultiple * winningBet.Amount);
                    baseWinnings += amountBack;
                    output += winningBet.BetNumberChoice != null
                        ? $"{winningBet.BetNumberChoice.ToString().Replace("-1", "00")}: ${FormatHelper.GetCommaNumber(winningBet.Amount)} -> ${FormatHelper.GetCommaNumber(amountBack)}\n"
                        : $"{winningBet.RoulleteBetType}: ${FormatHelper.GetCommaNumber(winningBet.Amount)} -> ${FormatHelper.GetCommaNumber(amountBack)}\n";
                }

                var betResolve = await _betManager.ResolveBet(userId, message.Author.Username, inputMoney, baseWinnings, isFirstGameOfTheDay);
                output += $"(+ bonus of ${ FormatHelper.GetCommaNumber(betResolve.BonusWinnings)})\n";
                output += $"{message.Author.Mention} `Your networth is now ${FormatHelper.GetCommaNumber(coinAccount.NetWorth)}`";
            }

            if (wasBonusGranted)
                output += $"\n\n*You will get a bonus $1000 + 10% net worth each hour for the rest of the day (UTC).*";

            await message.Channel.SendMessageAsync(output);

        }

        private async Task GameBlackjack(DiscordSocketClient client, SocketMessage message, List<string> args)
        {
            var playerId = message.Author.Id;

            //args valid inputs:
            //1000|any numbers - create or join game with 1000 as your bet
            //s|stay - blackjack stay action
            //h|hit - blackjack hit action
            //start - start the game without waiting

            if (args.Count != 1)
                throw new BadSyntaxException();

            if (_blackjackManager.TryGetPlayer(playerId, out var player)
                && player.IsFinishedPlaying) //IsFinishedPlaying will be set to true if player is in a game but it has not finished. 
            {
                await message.SendRichEmbedMessage($"You are still in a game but your turn is over. Once it ends and bets are calculated you can join another game.");
                return;
            }

            CoinAccount coinAccount = await _coinService.Get(playerId, message.Author.Username);
            
            if (args[0].StartsWith("start"))//'.bj start'
            {
                await _blackjackManager.Start(playerId, message);
            }
            else if (args[0].StartsWith("stay"))//'.bj stay'
            {
                await _blackjackManager.Stay(playerId, message);
            }
            else if (args[0].StartsWith("hit"))//'.bj hit'
            {
                await _blackjackManager.Hit(playerId, message);
            }
            else if (TryExtractBetAmount(args, coinAccount, out double betAmount)) //'.bj 1000'
            {
                await _blackjackManager.CreateOrJoin(playerId, betAmount, message); //will throw an exception if player already in a game, don't need to check
            }
        }

        private bool TryExtractBetAmount(IEnumerable<string> args, CoinAccount coinAccount, out double betAmount, int betAmountIndex = 0)
        {
            betAmount = 0;
            try
            {
                int minPercentBetRequired = 10;
                double tenPercentNw = coinAccount.NetWorth * ((double)minPercentBetRequired / 100);

                if (args.Count() == betAmountIndex || args.ElementAt(betAmountIndex).StartsWith("min"))
                    betAmount = tenPercentNw;
                else if (args.ElementAt(betAmountIndex).StartsWith("max"))
                    betAmount = coinAccount.NetWorth;
                else if (args.ElementAt(betAmountIndex).EndsWith('%'))
                {
                    double amountMultiple = Convert.ToDouble(args.ElementAt(betAmountIndex).TrimEnd('%')) / 100;
                    betAmount = coinAccount.NetWorth * amountMultiple;
                }
                else
                    betAmount = Convert.ToDouble(args.ElementAt(betAmountIndex));

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
