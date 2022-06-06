using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using DataAccess;
using WebSocket;
using LogAccess;

namespace LiveUpdate
{
    public static class UpdateProcessor
    {
        private const int INITIAL_PAUSE = 10 * 1000;    // milliseconds

        private static List<AssetPair> AssetPairs;

        public static bool IsInitialized = false;

        public static void Initialize()
        {
            Log.Information("Preparing live update data...");

            try
            {
                using (var dbContext = Database.GetContext())
                {
                    // Should be the same as in OrderBookDepth:
                    AssetPairs = dbContext.AssetPair.Where(a => a.Status == Database.STATUS_ACTIVE &&
                                                           (a.IsInternal || (a.Exchange.IsBook && a.Exchange.Status == Database.STATUS_ACTIVE)))
                                                    .Include("AssetOne").Include("AssetTwo").ToList();

                    OrderBookDepth.Initialize(dbContext);
                    OrderBookChart.Initialize(dbContext);
                    TradeHistory.Initialize(dbContext);
                    TradeHistoryChart.Initialize(dbContext);
                    TradeQuotes.Initialize(dbContext);   // needs to be after TradeHistoryChart
                }

                IsInitialized = true;

                Task.Run(() => ProcessOrderBookUpdates());
                Task.Run(() => ProcessOrderBookChartUpdates());
                Task.Run(() => ProcessTradeHistoryUpdates());
                Task.Run(() => ProcessTradeHistoryChartUpdates());
                Task.Run(() => ProcessQuoteUpdates());
                Log.Information("Live update initialized");
            }
            catch (Exception e)
            {
                Log.Error($"UpdateProcessor.Initialize failed. Error: \"{e.Message}\"");
            }
        }

        private static void ProcessOrderBookUpdates()
        {
            Thread.Sleep(INITIAL_PAUSE);

            while (true)
            {
                SendOrderBookUpdates();
                Thread.Sleep(100);
            }
        }

        private static void ProcessOrderBookChartUpdates()
        {
            Thread.Sleep(INITIAL_PAUSE);

            while (true)
            {
                SendOrderBookChartUpdates();
                Thread.Sleep(1000);
            }
        }

        private static void ProcessTradeHistoryUpdates()
        {
            Thread.Sleep(INITIAL_PAUSE);

            while (true)
            {
                SendTradeHistoryUpdates();
                Thread.Sleep(100);
            }
        }

        private static void ProcessTradeHistoryChartUpdates()
        {
            Thread.Sleep(INITIAL_PAUSE);

            while (true)
            {
                SendTradeHistoryChartUpdates();
                Thread.Sleep(1000);
            }
        }

        private static void ProcessQuoteUpdates()
        {
            Thread.Sleep(INITIAL_PAUSE);

            while (true)
            {
                SendQuoteUpdates();
                Thread.Sleep(100);
            }
        }

        private static void SendOrderBookUpdates()
        {
            try
            {
                using (var dbContext = Database.GetContext())
                {
                    SendOrderBookUpdates(dbContext);
                }
            }
            catch (Exception e)
            {
                Log.Error($"UpdateProcessor.SendOrderBookUpdates failed. Error: \"{e.Message}\"");
            }
        }

        private static void SendOrderBookChartUpdates()
        {
            try
            {
                using (var dbContext = Database.GetContext())
                {
                    SendOrderBookChartUpdates(dbContext);
                }
            }
            catch (Exception e)
            {
                Log.Error($"UpdateProcessor.SendOrderBookChartUpdates failed. Error: \"{e.Message}\"");
            }
        }

        private static void SendTradeHistoryUpdates()
        {
            try
            {
                using (var dbContext = Database.GetContext())
                {
                    SendTradeHistoryUpdates(dbContext);
                }
            }
            catch (Exception e)
            {
                Log.Error($"UpdateProcessor.SendTradeHistoryUpdates failed. Error: \"{e.Message}\"");
            }
        }

        private static void SendTradeHistoryChartUpdates()
        {
            try
            {
                using (var dbContext = Database.GetContext())
                {
                    SendTradeHistoryChartUpdates(dbContext);
                }
            }
            catch (Exception e)
            {
                Log.Error($"UpdateProcessor.SendTradeHistoryChartUpdates failed. Error: \"{e.Message}\"");
            }
        }

        private static void SendQuoteUpdates()
        {
            try
            {
                using (var dbContext = Database.GetContext())
                {
                    SendQuoteUpdates(dbContext);
                }
            }
            catch (Exception e)
            {
                Log.Error($"UpdateProcessor.SendQuoteUpdates failed. Error: \"{e.Message}\"");
            }
        }

        private static void SendOrderBookUpdates(DatabaseContext dbContext)
        {
            foreach (var assetPair in AssetPairs)
            {
                var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

                if (OrderBookDepth.Bids.ContainsKey(pairString) && OrderBookDepth.Asks.ContainsKey(pairString))
                {
                    var data = OrderBookDepth.GetUpdatedDepths(pairString).Result;

                    if (data["Bids"].Count > 0 || data["Asks"].Count > 0) SocketPusher.SendOrderBookUpdate(dbContext, pairString, data);
                }
            }
        }

        private static void SendTradeHistoryUpdates(DatabaseContext dbContext)
        {
            foreach (var assetPair in AssetPairs)
            {
                var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

                if (TradeHistory.TradeFillData.ContainsKey(pairString))
                {
                    var fills = TradeHistory.GetUpdatedFills(pairString).Result;

                    if (fills.Count > 0) SocketPusher.SendTradeHistoryUpdate(dbContext, pairString, fills);
                }
            }
        }

        private static void SendOrderBookChartUpdates(DatabaseContext dbContext)
        {
            foreach (var assetPair in AssetPairs)
            {
                var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

                if (OrderBookChart.Bids.ContainsKey(pairString) && OrderBookChart.Asks.ContainsKey(pairString))
                {
                    var data = OrderBookChart.GetUpdatedColumns(pairString).Result;

                    if (data["Bids"].Count > 0 || data["Asks"].Count > 0) SocketPusher.SendOrderBookChartUpdate(dbContext, pairString, data);
                }
            }
        }

        private static void SendTradeHistoryChartUpdates(DatabaseContext dbContext)
        {
            foreach (var assetPair in AssetPairs)
            {
                var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

                for (var i = 0; i < TradeHistoryChart.IntervalData.Length; i++)
                {
                    if (TradeHistoryChart.IntervalData[i].ContainsKey(pairString))
                    {
                        var candles = TradeHistoryChart.GetUpdatedCandles(dbContext, assetPair, i).Result;

                        if (candles.Count > 0) SocketPusher.SendTradeHistoryChartUpdate(dbContext, pairString, i, candles);
                    }
                }
            }
        }

        private static void SendQuoteUpdates(DatabaseContext dbContext)
        {
            var quotes = TradeQuotes.GetUpdatedQuotes(dbContext);

            if (quotes.Count > 0) SocketPusher.SendTradeQuotesUpdate(dbContext, quotes);
        }
    }
}
