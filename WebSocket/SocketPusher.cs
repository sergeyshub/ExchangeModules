using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using DataModels;
using DataAccess;
using ConfigAccess;
using HelpersLib;
using LogAccess;

namespace WebSocket
{
    public static class SocketPusher
    {
        public const string SOCKET_ROUTE = "/socket";
        public static Dictionary<string, decimal> VolumeTradeHistoryUpdateChart;

        private static IHubContext<WebSocketHub> hubContext = null;

        public static void Initialize(IApplicationBuilder app)
        {
            hubContext = app.ApplicationServices.GetRequiredService<IHubContext<WebSocketHub>>();
            VolumeTradeHistoryUpdateChart = new Dictionary<string, decimal>();
        }

        public static void SendTradeQuotesUpdate(DatabaseContext dbContext, List<QuoteHistoryItem> quotes)
        {
            var groupName = "quotes";

            // Log.Information($"SendTradeQuotesUpdate: {JsonConvert.SerializeObject(quotes)}.");

            var socketConnection = dbContext.SocketConnection.FirstOrDefault(a => a.Group == groupName);
            if (socketConnection == null) return;

            if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to users");
            hubContext.Clients.Group(groupName).SendAsync("SendQuoteUpdate", quotes);
        }

        // This is sent if transaction was added or confirmed
        public static void SendAccountUpdate(DatabaseContext dbContext, Transaction transaction, bool isNew = false)
        {
            if (transaction.Account == null) dbContext.Entry(transaction).Reference("Account").Load();

            var groupName = "account_" + transaction.Account.Id;

            var socketConnections = dbContext.SocketConnection.Include("User").Where(a => a.Group == groupName).ToArray();

            for (var i = 0; i < socketConnections.Length; i++)
            {
                if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to user {socketConnections[i].User.Id}");
                hubContext.Clients.Client(socketConnections[i].ConnectionId).SendAsync("SendAccountUpdate",
                    PrepareTransactionOutput(dbContext, transaction, isNew));
            }
        }

        public static void SendTradeUpdate(DatabaseContext dbContext, Trade trade)
        {
            if (trade.AccountOne == null) dbContext.Entry(trade).Reference("AccountOne").Load();
            if (trade.AccountTwo == null) dbContext.Entry(trade).Reference("AccountTwo").Load();
            if (trade.AccountOne == null && trade.AccountTwo == null) return;

            if (trade.AssetPair == null) dbContext.Entry(trade).Reference("AssetPair").Load();
            if (trade.AssetPair.AssetOne == null) dbContext.Entry(trade.AssetPair).Reference("AssetOne").Load();
            if (trade.AssetPair.AssetTwo == null) dbContext.Entry(trade.AssetPair).Reference("AssetTwo").Load();

            // Find all users, whose accounts were affected by this trade fill
            var userIds = GetTradeUserIds(dbContext, trade);

            foreach (var userId in userIds)
            {
                var groupName = "trade_" + userId;

                var socketConnections = dbContext.SocketConnection.Include("User").Where(a => a.Group == groupName).ToArray();

                for (var i = 0; i < socketConnections.Length; i++)
                {
                    if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to user {socketConnections[i].User.Id}");
                    hubContext.Clients.Client(socketConnections[i].ConnectionId).SendAsync("SendTradeUpdate", PrepareTradeOutput(trade));
                }
            }
        }

        public static void SendTradeFillUpdate(DatabaseContext dbContext, TradeFill tradeFill)
        {
            if (tradeFill.TradeOne == null) dbContext.Entry(tradeFill).Reference("TradeOne").Load();
            if (tradeFill.TradeTwo == null) dbContext.Entry(tradeFill).Reference("TradeTwo").Load();

            if (tradeFill.TradeOne.AccountOne == null) dbContext.Entry(tradeFill.TradeOne).Reference("AccountOne").Load();
            if (tradeFill.TradeOne.AccountTwo == null) dbContext.Entry(tradeFill.TradeOne).Reference("AccountTwo").Load();
            if (tradeFill.TradeOne.AccountOne == null && tradeFill.TradeOne.AccountTwo == null) return;

            if (tradeFill.TradeTwo != null && tradeFill.TradeTwo.AccountOne == null) dbContext.Entry(tradeFill.TradeTwo).Reference("AccountOne").Load();
            if (tradeFill.TradeTwo != null && tradeFill.TradeTwo.AccountTwo == null) dbContext.Entry(tradeFill.TradeTwo).Reference("AccountTwo").Load();

            if (tradeFill.TradeOne.AssetPair == null) dbContext.Entry(tradeFill.TradeOne).Reference("AssetPair").Load();
            if (tradeFill.TradeOne.AssetPair.AssetOne == null) dbContext.Entry(tradeFill.TradeOne.AssetPair).Reference("AssetOne").Load();
            if (tradeFill.TradeOne.AssetPair.AssetTwo == null) dbContext.Entry(tradeFill.TradeOne.AssetPair).Reference("AssetTwo").Load();

            // Find all users, whose accounts were affected by this trade fill
            var userIds = GetTradeFillUserIds(dbContext, tradeFill);

            foreach (var userId in userIds)
            {
                var groupName = "tradefills_" + userId;

                var socketConnections = dbContext.SocketConnection.Include("User").Where(a => a.Group == groupName).ToArray();

                for (var i = 0; i < socketConnections.Length; i++)
                {
                    if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to user {socketConnections[i].User.Id}");
                    hubContext.Clients.Client(socketConnections[i].ConnectionId).SendAsync("SendTradeFillsUpdate",
                        PrepareTradeFillOutput(tradeFill));
                }
            }
        }

        public static void SendOrderBookUpdate(DatabaseContext dbContext, string pairString, Dictionary<string, List<OrderBookItem>> data)
        {
            var groupName = "orderbook_" + pairString;

            // Log.Information($"SendOrderBookUpdate: {JsonConvert.SerializeObject(data)}.");

            var socketConnection = dbContext.SocketConnection.FirstOrDefault(a => a.Group == groupName);
            if (socketConnection == null) return;

            if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to users");
            hubContext.Clients.Group(groupName).SendAsync("SendOrderBookUpdate", data);
        }

        public static void SendOrderBookChartUpdate(DatabaseContext dbContext, string pairString, Dictionary<string, List<OrderBookChartColumn>> data)
        {
            var groupName = "orderbookchart_" + pairString;

            // Log.Information($"SendOrderBookChartUpdate: {JsonConvert.SerializeObject(data)}.");

            var socketConnection = dbContext.SocketConnection.FirstOrDefault(a => a.Group == groupName);
            if (socketConnection == null) return;

            if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to users");
            hubContext.Clients.Group(groupName).SendAsync("SendOrderBookChartUpdate", data);
        }

        public static void SendTradeHistoryUpdate(DatabaseContext dbContext, string pairString, List<TradeHistoryItem> tradeHistories)
        {
            var groupName = "tradehistory_" + pairString;

            var socketConnection = dbContext.SocketConnection.FirstOrDefault(a => a.Group == groupName);
            if (socketConnection == null) return;

            if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to users");
            hubContext.Clients.Group(groupName).SendAsync("SendTradeHistoryUpdate", tradeHistories);
        }

        public static void SendTradeHistoryChartUpdate(DatabaseContext dbContext, string pairString, int intervalId, List<TradeHistoryCandleItem> candles)
        {
            var groupName = "tradehistorychart_" + pairString + "_" + intervalId;

            // Log.Information($"SendTradeHistoryChartUpdate: {JsonConvert.SerializeObject(candles)}.");

            // Check if anyone is listening to this pair
            var socketConnection = dbContext.SocketConnection.FirstOrDefault(a => a.Group == groupName);
            if (socketConnection == null) return;

            if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to users");
            hubContext.Clients.Group(groupName).SendAsync("SendTradeHistoryChartUpdate", candles);
        }

        public static void SendMessagesUpdate(DatabaseContext dbContext, User user, string subject)
        {
            if (hubContext == null) return;

            var groupName = "messages_" + user.Id;

            var socketConnection = dbContext.SocketConnection.FirstOrDefault(a => a.Group == groupName);
            if (socketConnection == null) return;

            if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to users");
            hubContext.Clients.Group(groupName).SendAsync("SendMessagesUpdate", subject);
        }

        public static void SendPaymentAdded(DatabaseContext dbContext, PeerPayment peerPayment)
        {
            if (hubContext == null) return;

            if (peerPayment.PeerTradeFill == null) dbContext.Entry(peerPayment).Reference(a => a.PeerTradeFill).Load();
            if (peerPayment.PeerTradeFill.PeerOffer == null) dbContext.Entry(peerPayment.PeerTradeFill).Reference(a => a.PeerOffer).Load();
            if (peerPayment.PeerTradeFill.PaymentMethodType == null) dbContext.Entry(peerPayment.PeerTradeFill).Reference(a => a.PaymentMethodType).Load();

            // TODO: this does not work if payee is taker
            var user = peerPayment.CreatedBy == peerPayment.User ? peerPayment.Payee : peerPayment.User;

            var groupName = "peerpayment_" + user.Id;

            var socketConnection = dbContext.SocketConnection.FirstOrDefault(a => a.Group == groupName);
            if (socketConnection == null) return;

            if (Config.TraceSocketUpdates) Log.Information($"WebSocket: sending {groupName} to users");
            hubContext.Clients.Group(groupName).SendAsync("SendPeerPayment", PreparePeerPaymentOutput(peerPayment));
        }

        private static List<int> GetTradeUserIds(DatabaseContext dbContext, Trade trade)
        {
            var accounts = new List<Account>
            {
                trade.AccountOne,
                trade.AccountTwo
            };

            var userIds = new List<int>();

            foreach (var account in accounts)
            {
                var query = $"SELECT User.* FROM User, AccountUserRole WHERE User.Id = AccountUserRole.UserId AND AccountUserRole.AccountId = {account.Id} AND User.Status = {Database.STATUS_ACTIVE} GROUP BY User.Id";
                var users = dbContext.User.FromSql(query).ToList();
                foreach (var user in users) if (!userIds.Contains(user.Id)) userIds.Add(user.Id);
            }

            return userIds;
        }

        private static List<int> GetTradeFillUserIds(DatabaseContext dbContext, TradeFill tradeFill)
        {
            var accounts = new List<Account>{
                tradeFill.TradeOne.AccountOne,
                tradeFill.TradeOne.AccountTwo
            };

            if (tradeFill.TradeTwo != null)
            {
                accounts.Add(tradeFill.TradeTwo.AccountOne);
                accounts.Add(tradeFill.TradeTwo.AccountTwo);
            }

            var userIds = new List<int>();

            foreach (var account in accounts)
            {
                var query = $"SELECT User.* FROM User, AccountUserRole WHERE User.Id = AccountUserRole.UserId AND AccountUserRole.AccountId = {account.Id} AND User.Status = {Database.STATUS_ACTIVE} GROUP BY User.Id";
                var users = dbContext.User.FromSql(query).ToList();
                foreach (var user in users) if (!userIds.Contains(user.Id)) userIds.Add(user.Id);
            }

            return userIds;
        }

        private static IDictionary<string, object> PrepareTransactionOutput(DatabaseContext dbContext, Transaction transaction, bool isNew = false)
        {
            var dictionary = new Dictionary<string, object>();

            dictionary.Add("Type", transaction.Type);
            dictionary.Add("Amount", transaction.Amount);
            dictionary.Add("BalanceAfter", transaction.BalanceAfter);
            dictionary.Add("Number", transaction.Number);
            dictionary.Add("TimeAdded", transaction.TimeAdded);
            dictionary.Add("TimeExecuted", transaction.TimeExecuted);
            dictionary.Add("Status", transaction.Status);

            if (transaction.Account.Site != null) dictionary.Add("SiteName", transaction.Account.Site.Name);
            dictionary.Add("AccountNumber", transaction.Account.Number);
            if (transaction.Parent != null) dictionary.Add("ParentNumber", transaction.Parent.Number);

            var transactionCrypto = dbContext.TransactionCrypto.FirstOrDefault(a => a.UserTx == transaction);
            if (transactionCrypto != null)
            {
                var cryptoDictionary = new Dictionary<string, object>();

                cryptoDictionary.Add("Address", transactionCrypto.Address);
                if (transactionCrypto.AddressExt != null) cryptoDictionary.Add("AddressExt", transactionCrypto.AddressExt);
                cryptoDictionary.Add("TxId", transactionCrypto.CryptoTxId);
                cryptoDictionary.Add("TimeExecuted", transactionCrypto.TimeExecuted);
                cryptoDictionary.Add("Status", transactionCrypto.Status);

                dictionary.Add("Crypto", cryptoDictionary);
            }

            dictionary.Add("IsNew", isNew);

            return dictionary;
        }

        private static IDictionary<string, object> PrepareTradeOutput(Trade trade)
        {
            var dictionary = new Dictionary<string, object>();

            dictionary.Add("IsBuy", trade.IsBuy);
            dictionary.Add("Quantity", trade.Quantity);
            dictionary.Add("QuantityLeft", trade.QuantityLeft);
            dictionary.Add("QuantityReserved", trade.QuantityReserved);
            dictionary.Add("PriceLimit", trade.PriceLimit);
            dictionary.Add("PriceStop", trade.PriceStop);
            dictionary.Add("PriceTrailing", trade.PriceTrailing);
            dictionary.Add("TimeAdded", trade.TimeAdded);
            dictionary.Add("TimeCompleted", trade.TimeCompleted);
            dictionary.Add("Status", trade.Status);

            dictionary.Add("AssetPair", trade.AssetPair.AssetOne.Code + "-" + trade.AssetPair.AssetTwo.Code);
            dictionary.Add("Action", (trade.IsBuy) ? "buy" : "sell");
            dictionary.Add("AccountOne", trade.AccountOne.Number);
            dictionary.Add("AccountTwo", trade.AccountTwo.Number);

            return dictionary;
        }

        private static IDictionary<string, object> PrepareTradeFillOutput(TradeFill tradeFill)
        {
            var dictionary = new Dictionary<string, object>();

            dictionary.Add("Quantity", tradeFill.Quantity);
            dictionary.Add("Price", tradeFill.Price);
            dictionary.Add("TimeAdded", tradeFill.TimeAdded);
            dictionary.Add("Status", tradeFill.Status);

            dictionary.Add("AssetPair", tradeFill.TradeOne.AssetPair.AssetOne.Code + "-" + tradeFill.TradeOne.AssetPair.AssetTwo.Code);

            return dictionary;
        }

        private static Dictionary<string, object> PreparePeerPaymentOutput(PeerPayment peerPayment)
        {
            var peerTradeFill = peerPayment.PeerTradeFill;
            var paymentMethodType = peerTradeFill.PaymentMethodType;

            var dictionary = new Dictionary<string, object>();

            dictionary.Add("PeerTradeFillNumber", peerTradeFill.Number);
            dictionary.Add("PaymentProcessor", paymentMethodType.Name);
            dictionary.Add("Type", peerPayment.Type);
            dictionary.Add("TimeAdded", peerPayment.TimeAdded.ToString("yyyy-MM-dd HH:mm:ss"));
            dictionary.Add("AmountTrade", peerTradeFill.GetTotalPayment());

            if (peerPayment.Amount != null) dictionary.Add("AmountPayment", peerPayment.Amount);

            return dictionary;
        }
    }
}
