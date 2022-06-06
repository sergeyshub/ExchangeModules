using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using DataAccess;
using ConfigAccess;
using LogAccess;

namespace WebSocket
{
    public class WebSocketHub : Hub
    {
        protected DatabaseContext DbContext;
        protected User AppUser;
        
        public WebSocketHub(DatabaseContext dbContext)
        {
            DbContext = dbContext;
        }

        [AllowAnonymous]
        public override Task OnConnectedAsync()
        {
            if (Config.TraceSocketCalls) Log.Information($"WebSocket: Connected {Context.ConnectionId}");

            // Thread.Sleep(1000);

            return base.OnConnectedAsync();
        }

        [AllowAnonymous]
        public override Task OnDisconnectedAsync(Exception exception)
        {
            // Thread.Sleep(1000);

            try
            {
                var socketConnections = DbContext.SocketConnection.Where(a => a.ConnectionId == Context.ConnectionId);

                DbContext.SocketConnection.RemoveRange(socketConnections);
                DbContext.SaveChanges();

                if (Config.TraceSocketCalls) Log.Information($"WebSocket: Disconnected {Context.ConnectionId}");
            }
            catch (Exception e)
            {
                Log.Error($"WebSocket.OnDisconnectedAsync failed. Error: \"{e.Message}\"");
            }

            return base.OnDisconnectedAsync(exception);
        }

        [AllowAnonymous]
        public async Task StartQuoteUpdates()
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "quotes";

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StopQuoteUpdates()
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "quotes";

            RemoveSocketConnection(groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StartAccountUpdates(string AccountNumber)
        {
            // Thread.Sleep(1000);

            if (GetAppUser() == null)
            {
                await Clients.Caller.SendAsync("SendError", "Unauthorized");
                return;
            }

            var account = DbContext.Account.Include("Asset").FirstOrDefault(a => a.Number == AccountNumber);
            if (account == null)
            {
                await Clients.Caller.SendAsync("SendError", $"Account {AccountNumber} not found.");
                return;
            }

            if (!AppUser.HasAccessToAccount(DbContext, account, false, false))
            {
                await Clients.Caller.SendAsync("SendError", "Unauthorized");
                return;
            }

            string groupName = "account_" + account.Id;

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StopAccountUpdates(string AccountNumber)
        {
            // Thread.Sleep(1000);

            GetAppUser();

            if (string.IsNullOrEmpty(AccountNumber)) return;

            var account = DbContext.Account.Include("Asset").FirstOrDefault(a => a.Number == AccountNumber);
            if (account == null)
            {
                await Clients.Caller.SendAsync("SendError", $"Account {AccountNumber} not found.");
                return;
            }

            string groupName = "account_" + account.Id;

            RemoveSocketConnection(groupName);

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StartTradeUpdates()
        {
            // Thread.Sleep(1000);

            if (GetAppUser() == null)
            {
                await Clients.Caller.SendAsync("SendError", "Unauthorized");
                return;
            }

            var groupName = "trade_" + AppUser.Id;

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StopTradeUpdates()
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "trade_" + AppUser.Id;

            RemoveSocketConnection(groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StartTradeFillUpdates()
        {
            // Thread.Sleep(1000);

            if (GetAppUser() == null)
            {
                await Clients.Caller.SendAsync("SendError", "Unauthorized");
                return;
            }

            var groupName = "tradefills_" + AppUser.Id;

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StopTradeFillUpdates()
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "tradefills_" + AppUser.Id;

            RemoveSocketConnection(groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StartTradeHistoryUpdates(string pairString)
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "tradehistory_" + pairString;

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StopTradeHistoryUpdates(string pairString)
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "tradehistory_" + pairString;

            RemoveSocketConnection(groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        [AllowAnonymous]
        public async Task StartTradeHistoryChartUpdates(string pairString, int intervalId)
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "tradehistorychart_" + pairString + "_" + intervalId;

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [AllowAnonymous]
        public async Task StopTradeHistoryChartUpdates(string pairString, int intervalId)
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "tradehistorychart_" + pairString + "_" + intervalId;

            RemoveSocketConnection(groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StartOrderBookUpdates(string pairString)
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "orderbook_" + pairString;

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StopOrderBookUpdates(string pairString)
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "orderbook_" + pairString;

            RemoveSocketConnection(groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StartOrderBookChartUpdates(string pairString)
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "orderbookchart_" + pairString;

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StopOrderBookChartUpdates(string pairString)
        {
            // Thread.Sleep(1000);

            GetAppUser();

            var groupName = "orderbookchart_" + pairString;

            RemoveSocketConnection(groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StartMessagesUpdates()
        {
            if (GetAppUser() == null)
            {
                await Clients.Caller.SendAsync("SendError", "Unauthorized");
                return;
            }

            string groupName = "messages_" + AppUser.Id;

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StartPeerPaymentUpdates()
        {
            if (GetAppUser() == null)
            {
                await Clients.Caller.SendAsync("SendError", "Unauthorized");
                return;
            }

            var groupName = "peerpayment_" + AppUser.Id;

            AddSocketConnection(groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StopPeerPaymentUpdates()
        {
            GetAppUser();

            var groupName = "peerpayment_" + AppUser.Id;

            RemoveSocketConnection(groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        [Authorize(Roles = "User")]
        public async Task StopMessagesUpdates()
        {
            // Thread.Sleep(1000);

            GetAppUser();

            string groupName = "messages_" + AppUser.Id;

            RemoveSocketConnection(groupName);

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        private User GetAppUser()
        {
            var subClaim = Context.User.FindFirst(ClaimTypes.NameIdentifier);

            if (subClaim == null)
            {
                AppUser = null;
            }
            else
            {
                var account = subClaim.Value;
                AppUser = DbContext.User.Where(a => a.Number == account && a.Status == Database.STATUS_ACTIVE).Include("Site").FirstOrDefault();
            }

            // TODO: check IP

            return AppUser;
        }

        private void AddSocketConnection(string groupName = null)
        {
            try
            {
                string ipAddress = null, userName = null;

                var remoteIp = Context.GetHttpContext()?.Connection.RemoteIpAddress;
                if (remoteIp != null) ipAddress = remoteIp.ToString();

                if (AppUser != null) userName = $"{AppUser.Login}";

                var socketConnection = DbContext.SocketConnection.FirstOrDefault(a => a.ConnectionId == Context.ConnectionId && a.Group == groupName);

                if (socketConnection == null)
                {
                    socketConnection = new SocketConnection
                    {
                        User = AppUser,
                        ConnectionId = Context.ConnectionId,
                        Ip = ipAddress,
                        Group = groupName
                    };

                    DbContext.SocketConnection.Add(socketConnection);
                    DbContext.SaveChanges();

                    if (Config.TraceSocketCalls) Log.Information($"WebSocket: added {groupName} {Context.ConnectionId} {userName}");
                }
                else
                {
                    if (Config.TraceSocketCalls) Log.Information($"WebSocket: already added {groupName} {Context.ConnectionId} {userName}");
                }
            }
            catch (Exception e)
            {
                Log.Error($"WebSocket.AddSocketConnection failed. Error: \"{e.Message}\"");
            }
        }

        private void RemoveSocketConnection(string groupName)
        {
            try
            {
                string userName = null;

                if (AppUser != null) userName = $"{AppUser.Login}";

                var socketConnection = DbContext.SocketConnection.FirstOrDefault(a => a.Group == groupName && a.ConnectionId == Context.ConnectionId);
                if (socketConnection != null)
                {
                    DbContext.SocketConnection.Remove(socketConnection);
                    DbContext.SaveChanges();

                    if (Config.TraceSocketCalls) Log.Information($"WebSocket: removed {groupName} {Context.ConnectionId} {userName}");
                }
                else
                {
                    if (Config.TraceSocketCalls) Log.Information($"WebSocket: already removed {groupName} {Context.ConnectionId} {userName}");
                }
            }
            catch (Exception e)
            {
                Log.Error($"WebSocket.RemoveSocketConnection failed. Error: \"{e.Message}\"");
            }

            
        }
    }
}
