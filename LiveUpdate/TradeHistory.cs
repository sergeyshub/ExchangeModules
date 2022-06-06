using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using DataAccess;
using DataModels;
using LogAccess;

namespace LiveUpdate
{
    public static class TradeHistory
    {
        public static Dictionary<string, List<TradeHistoryItem>> TradeFillData = null;

        private static Dictionary<string, SemaphoreSlim> semaphores;

        public static void Initialize(DatabaseContext dbContext)
        {
            // Initialize all intervals for all pairs here

            TradeFillData = new Dictionary<string, List<TradeHistoryItem>>();

            var assetPairs = dbContext.AssetPair.Where(a => a.Status == Database.STATUS_ACTIVE && a.AssetOne.IsShow && a.AssetTwo.IsShow && (a.IsInternal || a.Exchange.IsBook)).Include("AssetOne").Include("AssetTwo").ToList();

            semaphores = new Dictionary<string, SemaphoreSlim>();

            foreach (var assetPair in assetPairs)
            {
                var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;
                var fills = new List<TradeHistoryItem>();
                TradeFillData.Add(pairString, fills);

                semaphores.Add(pairString, new SemaphoreSlim(1));
            }
        }

        public static async void AddTradeFill(DatabaseContext dbContext, AssetPair assetPair, decimal price, decimal quantity, DateTime timeExecuted)
        {
            // Log.Information($"TradeHistory.AddTradeFill pair: {assetPair.Id}, quanity: {quantity}, price: {price}, time: {timeExecuted}");

            if (TradeFillData == null) return;

            if (assetPair.AssetOne == null) dbContext.Entry(assetPair).Reference("AssetOne").Load();
            if (assetPair.AssetTwo == null) dbContext.Entry(assetPair).Reference("AssetTwo").Load();

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            if (!TradeFillData.ContainsKey(pairString)) return;

            await semaphores[pairString].WaitAsync();

            TradeFillData[pairString].Insert(0, new TradeHistoryItem()
            {
                TimeExecuted = timeExecuted,
                Price = price,
                Quantity = quantity,
                IsUpdated = true
            });

            semaphores[pairString].Release();
        }

        public static async Task<List<TradeHistoryItem>> GetUpdatedFills(string pairString)
        {
            const int MAX_TRADE_HISTORY_COUNT = 200;

            await semaphores[pairString].WaitAsync();

            var result = TradeFillData[pairString].FindAll(x => x.IsUpdated).Select(c => { c.IsUpdated = false; return c; }).ToList();

            if (MAX_TRADE_HISTORY_COUNT < TradeFillData[pairString].Count)
            {
                TradeFillData[pairString].RemoveRange(MAX_TRADE_HISTORY_COUNT, TradeFillData[pairString].Count - MAX_TRADE_HISTORY_COUNT);
            }

            semaphores[pairString].Release();

            return result;
        }
    }
}