﻿using Common.Helpers;
using Common.Models;
using Common.Services;
using Discord.WebSocket;
using DiscordBot.Managers;
using DiscordBot.Models;
using DiscordBot.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http.Headers;
using WebAlerter;

namespace DiscordBot
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpsRedirection(options => { options.HttpsPort = 443; });

            services.AddTransient<GistSettings>(sp =>
            {
                var settings = new GistSettings();
                settings.UserName = EnvironmentHelper.GetEnvironmentVariableOrThrow("GITHUB_USERNAME");
                settings.Id = EnvironmentHelper.GetEnvironmentVariableOrThrow("GITHUB_GIST_ID");
                return settings;
            });

            services.AddHttpClient<GistService>(h =>
            {
                h.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
                h.DefaultRequestHeaders.Add("User-Agent", "Pepsi-Dog-Bot");
                h.DefaultRequestHeaders.Add("Authorization", $"token { EnvironmentHelper.GetEnvironmentVariableOrThrow("GITHUB_PAT_TOKEN")}");
            });


            services.AddSingleton<DiscordSocketClient>();
            services.AddTransient<MappingService>();
            services.AddTransient<ReminderService>();
            services.AddTransient<CoinService>();
            services.AddSingleton<CommandManager>();
            services.AddSingleton<MessageHandler>();
            services.AddTransient<StrawmanChecker>();

            services.AddHttpClient<StrawmanChecker>(h =>
            {
                h.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
                h.DefaultRequestHeaders.Add("User-Agent", "Pepsi-Dog-Bot-2");
                h.DefaultRequestHeaders.Add("Authorization", $"token { EnvironmentHelper.GetEnvironmentVariableOrThrow("GITHUB_PAT_TOKEN")}");
            });

            services.AddHostedService<ChatWorker>();
            services.AddHostedService<WebAlerterWorker>();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseForwardedHeaders();

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IS_HEROKU")))
            {
                Console.WriteLine("Use https redirection");
                app.UseHttpsRedirection();
            }


            app
                .UseRouting()
                .UseDefaultFiles()
                .UseStaticFiles()
                .UseCors("CorsPolicy")
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapDefaultControllerRoute();
                });
        }
    }
}
