using dndhelper.Models;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class DiscordWebhookClient
    {
        private const int TableColor = 0x8B5CF6;
        private const int PollColor = 0xF5B041;
        private const int ResultColor = 0x2ECC71;

        private static readonly Regex WebhookUrl = new(
            @"^https://(?:(?:ptb|canary)\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w-]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly HttpClient _http;
        private readonly ILogger _logger;
        private readonly string _siteUrl;

        public DiscordWebhookClient(HttpClient http, IConfiguration config, ILogger logger)
        {
            _http = http;
            _logger = logger;
            _siteUrl = (config["SiteUrl"] ?? "https://dndreforged.quest").TrimEnd('/');
        }

        private static readonly Regex RoleMention = new(@"^(?:<@&)?(\d{17,20})>?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsValidWebhookUrl(string url) => WebhookUrl.IsMatch(url);

        public static string? NormalizeRoleId(string input)
        {
            input = input.Trim();
            if (input.Length == 0) return string.Empty;
            var match = RoleMention.Match(input);
            return match.Success ? match.Groups[1].Value : null;
        }

        public Task<bool> PostTableInviteAsync(string webhookUrl, string? roleId, string campaignName, string invitedBy, string joinCode)
        {
            var link = $"{_siteUrl}/table/{joinCode}";
            return PostAsync(webhookUrl, roleId, campaignName, Embed(
                TableColor,
                "🔔 The table is open!",
                link,
                $"**{invitedBy}** opened the **{campaignName}** table. Grab your dice!\n\n**[⚔️ Join the table]({link})**",
                campaignName,
                new { name = "Join code", value = $"`{joinCode}`", inline = true }));
        }

        public Task<bool> PostPollStartedAsync(string webhookUrl, string? roleId, string campaignName, string createdBy, Poll poll)
        {
            var link = $"{_siteUrl}/polls";
            var options = string.Join("\n", poll.Options.Select(o => $"▸ {o.Text}"));
            var fields = poll.ClosesAt is { } closes
                ? new object[] { new { name = "Closes", value = $"<t:{new DateTimeOffset(DateTime.SpecifyKind(closes, DateTimeKind.Utc)).ToUnixTimeSeconds()}:R>", inline = true } }
                : Array.Empty<object>();

            return PostAsync(webhookUrl, roleId, campaignName, Embed(
                PollColor,
                $"📊 {poll.Title}",
                link,
                $"**{createdBy}** wants the party's opinion.\n\n{options}\n\n**[🗳️ Cast your vote]({link})**",
                campaignName,
                fields));
        }

        public Task<bool> PostPollResultAsync(string webhookUrl, string? roleId, string campaignName, string title, string summary) =>
            PostAsync(webhookUrl, roleId, campaignName, Embed(
                ResultColor,
                $"🏁 Poll closed: {title}",
                $"{_siteUrl}/polls",
                $"🏆 **{summary}**",
                campaignName));

        private static object Embed(int color, string title, string url, string description, string campaignName, params object[] fields) => new
        {
            color,
            title,
            url,
            description,
            fields,
            footer = new { text = $"D&D Reforged · {campaignName}" },
            timestamp = DateTime.UtcNow.ToString("o"),
        };

        private async Task<bool> PostAsync(string webhookUrl, string? roleId, string campaignName, object embed)
        {
            if (!IsValidWebhookUrl(webhookUrl)) return false;

            var ping = string.IsNullOrEmpty(roleId) ? null : roleId;
            var payload = new
            {
                username = "D&D Reforged",
                content = ping == null ? "" : $"<@&{ping}>",
                embeds = new[] { embed },
                allowed_mentions = new { parse = Array.Empty<string>(), roles = ping == null ? Array.Empty<string>() : new[] { ping } },
            };

            try
            {
                using var response = await _http.PostAsJsonAsync(webhookUrl, payload);
                if (response.IsSuccessStatusCode) return true;
                _logger.Warning("Discord webhook for {Campaign} answered {Status}", campaignName, (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.Warning(ex, "Discord webhook for {Campaign} failed", campaignName);
            }
            return false;
        }
    }
}
