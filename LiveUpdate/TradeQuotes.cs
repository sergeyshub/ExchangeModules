using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using DataAccess;
using DataModels;
using LogAccess;
using HelpersLib;

namespace LiveUpdate
{
    public static class TradeQuotes
    {
        public static Dictionary<string, QuoteHistoryItem> QuoteData = null;

        private static bool IsInitilized = false;
        private static Dictionary<string, List<TradeHistoryCandleItem>> ChartInterval = null;
        private static Dictionary<string, int> AssetPairCodes;

        public static void Initialize(DatabaseContext dbContext)
        {
            var assetPairs = dbContext.AssetPair.Where(a => a.Status == Database.STATUS_ACTIVE).Include("Exchange").Include("AssetOne").Include("AssetTwo").ToList();

            for (var i = 0; i < TradeHistoryChart.IntervalTypes.Length; i++)
                if (TradeHistoryChart.IntervalTypes[i].Step == 3600 && 24 <= TradeHistoryChart.IntervalTypes[i].Length)
                {
                    ChartInterval = TradeHistoryChart.IntervalData[i];
                    break;
                }

            if (ChartInterval == null)
            {
                Log.Error($"LiveQuotes.Initialize could not find an hourly TradeHistoryChart interval of at least 24 in length.");
                return;
            }

            QuoteData = new Dictionary<string, QuoteHistoryItem>();
            AssetPairCodes = new Dictionary<string, int>();

            foreach (var assetPair in assetPairs) InitializeAssetPair(dbContext, assetPair);

            IsInitilized = true;
        }

        public static void AddTradeFill(DatabaseContext dbContext, AssetPair assetPair, decimal price, decimal quantity, DateTime timeExecuted)
        {
            if (!IsInitilized) return;
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            QuoteHistoryItem quoteItem;
            price = Math.Round(price, assetPair.AssetTwo.Decimals);

            if (!QuoteData.ContainsKey(pairString))
            {
                // This must be a "hidden" asset without a TradeHistoryChart
                quoteItem = new QuoteHistoryItem()
                {
                    PairString = pairString,
                    PriceFinish = price,
                    IsBook = false,
                    IsShow = false,
                    IsUpdated = true
                };

                QuoteData.Add(pairString, quoteItem);
                AssetPairCodes.Add(pairString, assetPair.Id);

                return;
            }

            quoteItem = GetTradingData(dbContext, pairString);

            if (quoteItem == null)
            {
                // This must be a "hidden" asset without a TradeHistoryChart
                QuoteData[pairString].PriceFinish = price;
                QuoteData[pairString].IsUpdated = true;

                return;
            }

            var isUpdated = false;

            if (QuoteData[pairString].PriceStart != quoteItem.PriceStart)
            {
                QuoteData[pairString].PriceStart = quoteItem.PriceStart;
                isUpdated = true;
            }

            if (QuoteData[pairString].PriceFinish != quoteItem.PriceFinish)
            {
                QuoteData[pairString].PriceFinish = quoteItem.PriceFinish;
                isUpdated = true;
            }

            if (QuoteData[pairString].PriceLow != quoteItem.PriceLow)
            {
                QuoteData[pairString].PriceLow = quoteItem.PriceLow;
                isUpdated = true;
            }

            if (QuoteData[pairString].PriceHigh != quoteItem.PriceHigh)
            {
                QuoteData[pairString].PriceHigh = quoteItem.PriceHigh;
                isUpdated = true;
            }

            if (QuoteData[pairString].Volume != quoteItem.Volume)
            {
                QuoteData[pairString].Volume = quoteItem.Volume;
                isUpdated = true;
            }

            QuoteData[pairString].IsUpdated = isUpdated;
        }

        public static List<QuoteHistoryItem> GetUpdatedQuotes(DatabaseContext dbContext)
        {
            var resultQuotes = new List<QuoteHistoryItem>();

            if (!IsInitilized) return resultQuotes;

            foreach (var quote in QuoteData)
            {
                if (quote.Value.IsUpdated)
                {
                    dbContext.ExecuteStatement($"UPDATE AssetPair SET Price = {MathEx.ToString(quote.Value.PriceFinish)} WHERE Id = {AssetPairCodes[quote.Key]}");
                    quote.Value.IsUpdated = false;

                    if (quote.Value.IsShow) resultQuotes.Add(quote.Value);
                }
            }

            return resultQuotes;
        }

        private static void InitializeAssetPair(DatabaseContext dbContext, AssetPair assetPair)
        {
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            var quoteItem = GetTradingData(dbContext, pairString);

            if (quoteItem == null)
            {
                // This must be a "hidden" or a spot exchange asset without a TradeHistoryChart

                if (assetPair.Price == null) throw new ApplicationException($"Price not set for asset pair '{pairString}'.");
                if (!assetPair.IsInternal && assetPair.Exchange == null) throw new ApplicationException($"Exchange not set for asset pair '{pairString}'.");

                quoteItem = new QuoteHistoryItem()
                {
                    PairString = pairString,
                    PriceFinish = (decimal)assetPair.Price,
                    IsBook = assetPair.IsInternal || assetPair.Exchange.IsBook,
                    IsShow = assetPair.AssetOne.IsShow && assetPair.AssetTwo.IsShow,
                };
            }
            else
            {
                quoteItem.IsBook = assetPair.IsInternal || assetPair.Exchange.IsBook;
                quoteItem.IsShow = assetPair.AssetOne.IsShow && assetPair.AssetTwo.IsShow;
            }

            quoteItem.IsUpdated = false;

            QuoteData.Add(pairString, quoteItem);

            AssetPairCodes.Add(pairString, assetPair.Id);
        }

        // Computes quote information from TradeHistoryChart, using the hourly interval
        private static QuoteHistoryItem GetTradingData(DatabaseContext dbContext, string pairString)
        {
            if (!ChartInterval.ContainsKey(pairString) || ChartInterval[pairString].Count == 0) return null;
            
            // Get the index of the beginning and end of the last 25 hours
            var first = Math.Max(0, ChartInterval[pairString].Count - 25);
            var last = ChartInterval[pairString].Count - 1;

            // Initialize priceLow, priceHigh, volume as the values of the first hour
            var priceLow = ChartInterval[pairString][first].PriceLow;
            var priceHigh = ChartInterval[pairString][first].PriceHigh;
            var volume = ChartInterval[pairString][first].Volume;

            // Add the volumes and compare the prices of the remaining 24 hours
            for (var i = first + 1; i <= last; i++)
            {
                if (priceLow > ChartInterval[pairString][i].PriceLow) priceLow = ChartInterval[pairString][i].PriceLow;
                if (priceHigh < ChartInterval[pairString][i].PriceHigh) priceHigh = ChartInterval[pairString][i].PriceHigh;
                volume += ChartInterval[pairString][i].Volume;
            }

            // Compose quoteItem based on the computed values
            var quoteItem = new QuoteHistoryItem()
            {
                PairString = pairString,
                PriceStart = ChartInterval[pairString][first].PriceStart,
                PriceFinish = ChartInterval[pairString][last].PriceFinish,
                PriceLow = priceLow,
                PriceHigh = priceHigh,
                Volume = volume
            };

            return quoteItem;
        }
    }
}