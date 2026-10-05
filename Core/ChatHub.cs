using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Core
{
    /// <summary>
    /// Real-time campaign chat, for any screen (the tabletop, the floating chat). A connection opens one campaign at a time;
    /// membership is checked then and kept on the connection. Groups per campaign: everyone, its DMs, and each user,
    /// so whispers reach only who may read them.
    /// </summary>
    [Authorize]
    public class ChatHub : Hub
    {
        private const string RoomKey = "chat";
        private readonly IChatService _chat;

        public ChatHub(IChatService chat)
        {
            _chat = chat;
        }

        private sealed record Seat(string CampaignId, ChatMember Member);

        private ChatCaller Caller => new(
            Context.UserIdentifier ?? throw new HubException("Not signed in."),
            Context.User?.Identity?.Name ?? "Player",
            Context.User?.IsInRole("Admin") == true);

        private Seat Room =>
            Context.Items.TryGetValue(RoomKey, out var value) && value is Seat seat
                ? seat
                : throw new HubException("Open a campaign chat first.");

        public Task<List<ChatCampaign>> Campaigns() => _chat.CampaignsAsync(Caller);

        /// <summary>Switches this connection to a campaign's chat and returns its newest page.</summary>
        public async Task<ChatRoom> Open(string campaignId)
        {
            var (member, room) = await Run(() => _chat.OpenAsync(Caller, campaignId));

            if (Context.Items.TryGetValue(RoomKey, out var previous) && previous is Seat old)
                foreach (var group in GroupsOf(old))
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);

            var seat = new Seat(room.CampaignId, member);
            Context.Items[RoomKey] = seat;
            foreach (var group in GroupsOf(seat))
                await Groups.AddToGroupAsync(Context.ConnectionId, group);
            return room;
        }

        public async Task Send(ChatSendRequest request)
        {
            var seat = Room;
            var message = await Run(() => _chat.SendAsync(seat.Member, seat.CampaignId, request));
            await Audience(message).SendAsync("ChatAdded", message);
        }

        public async Task Edit(string messageId, string text)
        {
            var seat = Room;
            var message = await Run(() => _chat.EditAsync(seat.Member, seat.CampaignId, messageId, text));
            await Audience(message).SendAsync("ChatUpdated", message);
        }

        public async Task Delete(string messageId)
        {
            var seat = Room;
            var message = await Run(() => _chat.DeleteAsync(seat.Member, seat.CampaignId, messageId));
            await Audience(message).SendAsync("ChatRemoved", message.Id);
        }

        /// <summary>The page of messages before the given one.</summary>
        public Task<ChatPage> History(string beforeId)
        {
            var seat = Room;
            return Run(() => _chat.PageAsync(seat.Member, seat.CampaignId, beforeId));
        }

        private static string All(string campaignId) => $"chat:{campaignId}";
        private static string Dms(string campaignId) => $"chat:{campaignId}:dm";
        private static string User(string campaignId, string userId) => $"chat:{campaignId}:u:{userId}";

        private static IEnumerable<string> GroupsOf(Seat seat)
        {
            yield return All(seat.CampaignId);
            yield return User(seat.CampaignId, seat.Member.UserId);
            if (seat.Member.IsDm) yield return Dms(seat.CampaignId);
        }

        /// <summary>Same rule as ChatMessage.VisibleTo: a whisper goes to the DMs, the sender and its target's owners. Clients drop repeats by id.</summary>
        private IClientProxy Audience(ChatMessage message)
        {
            if (!message.Whisper) return Clients.Group(All(message.CampaignId));

            var groups = new List<string> { Dms(message.CampaignId), User(message.CampaignId, message.UserId) };
            groups.AddRange((message.ToUserIds ?? new List<string>()).Select(id => User(message.CampaignId, id)));
            if (message.ToUserId != null) groups.Add(User(message.CampaignId, message.ToUserId));
            return Clients.Groups(groups);
        }

        /// <summary>Chat rule violations reach the caller as their message.</summary>
        private static async Task<T> Run<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (ArgumentException ex)
            {
                throw new HubException(ex.Message);
            }
        }
    }
}
