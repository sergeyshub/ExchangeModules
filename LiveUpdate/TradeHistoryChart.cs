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
    public class ChartInterval
    {
        /// Chart step in seconds
        public int Step { get; set; }

        /// Chart length in steps
        public int Length { get; set; }
    }

    public static class TradeHistoryChart
    {
        private const decimal VOLUME_FACTOR_INTERNAL = 1.00m;
        private const decimal VOLUME_FACTOR_EXTERNAL = 0.11m;

        private static Dictionary<string, SemaphoreSlim>[] semaphores;

        public static readonly ChartInterval[] IntervalTypes =
        {
            new ChartInterval { Step = 60 * 60, Length = 25},           // Step: 1 hour, Length: 1 day, 1 hour   
            new ChartInterval { Step = 60 * 60 * 24, Length = 30},      // Step: 1 day,  Length: 30 days 
            new ChartInterval { Step = 60 * 60 * 24 * 7, Length = 52}   // Step: 1 week, Length: 1 year
        };

        public static Dictionary<string, List<TradeHistoryCandleItem>>[] IntervalData = null;

        private static bool IsInitilized = false;

        public static void Initialize(DatabaseContext dbContext)
        {
            semaphores = new Dictionary<string, SemaphoreSlim>[IntervalTypes.Length];
            IntervalData = new Dictionary<string, List<TradeHistoryCandleItem>>[IntervalTypes.Length];

            for (int i = 0; i < IntervalData.Length; i++)
            {
                semaphores[i] = new Dictionary<string, SemaphoreSlim>();
                IntervalData[i] = new Dictionary<string, List<TradeHistoryCandleItem>>();
            }

            var assetPairs = dbContext.AssetPair.Where(a => a.Status == Database.STATUS_ACTIVE && a.AssetOne.IsShow && a.AssetTwo.IsShow).Include("AssetOne").Include("AssetTwo").Include("Exchange").ToList();

            foreach (var assetPair in assetPairs)
            {
                Log.Information($"Preparing pair: {assetPair.AssetOne.Code}-{assetPair.AssetTwo.Code}");

                for (int i = 0; i < IntervalData.Length; i++) InitializeInterval(dbContext, assetPair, i);
            }

            IsInitilized = true;
        }

        public static void AddTradeFill(DatabaseContext dbContext, AssetPair assetPair, decimal price, decimal quantity, DateTime timeExecuted)
        {
            // Log.Information($"TradeHistoryChart.AddTradeFill pair: {assetPair.Id}, quanity: {quantity}, price: {price}, time: {timeExecuted}");

            if (!IsInitilized) return;

            quantity *= (assetPair.IsInternal) ? VOLUME_FACTOR_INTERNAL : VOLUME_FACTOR_EXTERNAL;

            if (assetPair.AssetOne == null) dbContext.Entry(assetPair).Reference("AssetOne").Load();
            if (assetPair.AssetTwo == null) dbContext.Entry(assetPair).Reference("AssetTwo").Load();
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            price = Math.Round(price, assetPair.AssetTwo.Decimals);

            for (var i = 0; i < IntervalData.Length; i++)
                if (IntervalData[i].ContainsKey(pairString))
                    UpdateInterval(dbContext, i, pairString, price, quantity, timeExecuted);
        }

        public static async Task<List<TradeHistoryCandleItem>> GetUpdatedCandles(DatabaseContext dbContext, AssetPair assetPair, int intervalId)
        {
            List<TradeHistoryCandleItem> result;

            if (!IsInitilized) return new List<TradeHistoryCandleItem>();

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            await semaphores[intervalId][pairString].WaitAsync();

            try
            {
                var interval = IntervalData[intervalId][pairString];

                var lastCandle = interval.Last();
                if (lastCandle.TimeFinish <= DateTime.Now) UpdateChandles(dbContext, pairString, intervalId);

                result = interval.FindAll(x => x.IsUpdated).ToList();

                SaveCandles(dbContext, assetPair, intervalId, result);
            }
            finally
            {
                semaphores[intervalId][pairString].Release();
            }

            return result;
        }

        private static void SaveCandles(DatabaseContext dbContext, AssetPair assetPair, int intervalId, List<TradeHistoryCandleItem> candles)
        {
            if (candles.Count == 0) return;

            var conditionTime = $" AND TimeStart IN ('{candles[0].TimeStart.ToString("yyyy-MM-dd HH:mm:ss")}'";
            for (var i = 1; i < candles.Count; i++) conditionTime += $", '{candles[i].TimeStart.ToString("yyyy-MM-dd HH:mm:ss")}'";
            conditionTime += ")";

            var query = $"SELECT * FROM TradeHistoryCandle WHERE AssetPairId = {assetPair.Id} AND IntervalId = {intervalId} {conditionTime} ORDER BY TimeStart";
            // Log.Information($"TradeHistoryChart.SaveCandles query: {query}");
            var savedCandles = dbContext.TradeHistoryCandle.FromSql(query).ToList();

            // var matches = "";
            // foreach (var candle in savedCandles) matches += " " + candle.TimeStart.ToString("yyyy-MM-dd HH:mm:ss");
            // Log.Information($"TradeHistoryChart.SaveCandles found:{matches}");

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            foreach (var candle in candles)
            {
                var i = 0;
                var isFound = false;

                while (i < savedCandles.Count)
                {
                    // Weird time in the DB: "2018-10-10 20:02:12". Milliseconds in candle.TimeStart!
                    if (candle.TimeStart == savedCandles[i].TimeStart)
                    {
                        isFound = true;
                        break;
                    }
                    i++;
                }

                if (isFound)
                {
                    savedCandles[i].TimeFinish = (candle.TimeFinish < DateTime.Now) ? candle.TimeFinish : DateTime.Now;
                    savedCandles[i].PriceStart = candle.PriceStart;
                    savedCandles[i].PriceFinish = candle.PriceFinish;
                    savedCandles[i].PriceLow = candle.PriceLow;
                    savedCandles[i].PriceHigh = candle.PriceHigh;
                    savedCandles[i].Volume = candle.Volume;
                }
                else
                {
                    // Log.Information($"TradeHistoryChart.SaveCandles adding Pair: {assetPair.Id}, Interval: {intervalId}, TimeStart: {candle.TimeStart}");

                    dbContext.TradeHistoryCandle.Add(new TradeHistoryCandle()
                    {
                        AssetPairId = assetPair.Id,
                        IntervalId = intervalId,
                        TimeStart = candle.TimeStart,
                        TimeFinish = (candle.TimeFinish < DateTime.Now) ? candle.TimeFinish : DateTime.Now,
                        PriceStart = candle.PriceStart,
                        PriceFinish = candle.PriceFinish,
                        PriceLow = candle.PriceLow,
                        PriceHigh = candle.PriceHigh,
                        Volume = candle.Volume,
                    });
                }

                candle.IsUpdated = false;
            }

            dbContext.SaveChanges();
        }

        private static void InitializeInterval(DatabaseContext dbContext, AssetPair assetPair, int intervalId)
        {
            TradeHistoryCandleItem candle = null;
            var step = IntervalTypes[intervalId].Step;
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;
            var intervalStarts = ComputeIntervalStarts(dbContext, assetPair, intervalId);
            var candles = new List<TradeHistoryCandleItem>();
            decimal? previousPrice = null;

            semaphores[intervalId].Add(pairString, new SemaphoreSlim(1));

            if (intervalStarts.Length == 0)
            {
                IntervalData[intervalId].Add(pairString, candles);
                return;
            }

            var savedTradeHistory = dbContext.TradeHistoryCandle.Where(a => a.AssetPairId == assetPair.Id && a.IntervalId == intervalId && intervalStarts[0] <= a.TimeStart).OrderBy(a => a.TimeStart).ToList();

            for (var i = 0; i < intervalStarts.Length; i++)
            {
                var savedCandle = savedTradeHistory.Find(a => a.TimeStart == intervalStarts[i]);

                if (savedCandle == null)
                {
                    var timeFinish = intervalStarts[i];
                    timeFinish = timeFinish.AddSeconds(step);

                    if (previousPrice == null) previousPrice = FindPriceStart(dbContext, assetPair, intervalStarts[0], intervalStarts[0].AddSeconds(step));
                    candle = ComputeCandle(dbContext, assetPair, intervalStarts[i], timeFinish, (decimal)previousPrice);

                    var tradeHistoryCandle = new TradeHistoryCandle()
                    {
                        AssetPairId = assetPair.Id,
                        IntervalId = intervalId,
                        TimeStart = candle.TimeStart,
                        TimeFinish = (candle.TimeFinish < DateTime.Now) ? candle.TimeFinish : DateTime.Now,
                        PriceStart = candle.PriceStart,
                        PriceFinish = candle.PriceFinish,
                        PriceLow = candle.PriceLow,
                        PriceHigh = candle.PriceHigh,
                        Volume = candle.Volume
                    };

                    dbContext.TradeHistoryCandle.Add(tradeHistoryCandle);
                }
                else
                {
                    var timeFinish = intervalStarts[i];
                    timeFinish = timeFinish.AddSeconds(step);

                    candle = new TradeHistoryCandleItem()
                    {
                        TimeStart = intervalStarts[i],
                        TimeFinish = timeFinish,
                        PriceStart = savedCandle.PriceStart,
                        PriceFinish = savedCandle.PriceFinish,
                        PriceLow = savedCandle.PriceLow,
                        PriceHigh = savedCandle.PriceHigh,
                        Volume = savedCandle.Volume,
                        IsUpdated = false
                    };

                    if (savedCandle.TimeFinish != timeFinish)
                    {
                        var lastCandle = ComputeCandle(dbContext, assetPair, candle.TimeFinish, timeFinish, candle.PriceFinish);

                        candle.TimeFinish = lastCandle.TimeFinish;
                        candle.Volume = savedCandle.Volume = candle.Volume + lastCandle.Volume;
                        candle.PriceFinish = savedCandle.PriceFinish = lastCandle.PriceFinish;
                        if (lastCandle.PriceLow < candle.PriceLow) candle.PriceLow = savedCandle.PriceLow = lastCandle.PriceLow;
                        if (lastCandle.PriceHigh > candle.PriceHigh) candle.PriceHigh = savedCandle.PriceHigh = lastCandle.PriceHigh;
                        savedCandle.TimeFinish = (lastCandle.TimeFinish < DateTime.Now) ? lastCandle.TimeFinish : DateTime.Now;
                    }
                }

                candles.Add(candle);
                previousPrice = candle.PriceFinish;
            }

            IntervalData[intervalId].Add(pairString, candles);

            LogItemCount();

            dbContext.SaveChanges();
        }

        private static async void UpdateInterval(DatabaseContext dbContext, int intervalId, string pairString, decimal price, decimal quantity, DateTime timeExecuted)
        {
            var interval = IntervalData[intervalId][pairString];

            await semaphores[intervalId][pairString].WaitAsync();

            try
            {
                if (interval.Count == 0)
                {
                    var step = IntervalTypes[intervalId].Step;
                    var timeStart = RoundStartTime(timeExecuted, step);

                    // Log.Information($"TradeHistoryChart.UpdateInterval adding new candle pair: {pairString}, interval: {intervalId}, time: {timeStart}");

                    interval.Add(new TradeHistoryCandleItem()
                    {
                        TimeStart = timeStart,
                        TimeFinish = timeStart.AddSeconds(IntervalTypes[intervalId].Step),
                        PriceStart = price,
                        PriceFinish = price,
                        PriceLow = price,
                        PriceHigh = price,
                        Volume = quantity,
                        IsUpdated = true
                    });
                }

                var lastCandle = interval.Last();
                if (lastCandle.TimeFinish <= timeExecuted) UpdateChandles(dbContext, pairString, intervalId);

                int i;

                for (i = interval.Count - 1; i >= 0; i--)
                {
                    if (interval[i].TimeStart < timeExecuted)
                    {
                        var isUpdated = false;

                        if (quantity != 0)
                        {
                            interval[i].Volume += quantity;
                            isUpdated = true;
                        }

                        if (interval[i].PriceFinish != price)
                        {
                            interval[i].PriceFinish = price;
                            isUpdated = true;
                        }

                        if (price < interval[i].PriceLow)
                        {
                            interval[i].PriceLow = price;
                            isUpdated = true;
                        }

                        if (price > interval[i].PriceHigh)
                        {
                            interval[i].PriceHigh = price;
                            isUpdated = true;
                        }

                        interval[i].IsUpdated = isUpdated;

                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"TradeHistoryChart.UpdateInterval failed. Error: \"{e.Message}\". Details: \"{e.InnerException}\".");
            }
            finally
            {
                semaphores[intervalId][pairString].Release();
            }
        }

        private static void UpdateChandles(DatabaseContext dbContext, string pairString, int intervalId)
        {
            var length = IntervalTypes[intervalId].Length;
            var step = IntervalTypes[intervalId].Step;

            var candlesLast = IntervalData[intervalId][pairString].Last();

            var timeStart = candlesLast.TimeFinish;

            while (timeStart < DateTime.Now)
            {
                var timeFinish = timeStart.AddSeconds(step);

                // Log.Information($"TradeHistoryChart.UpdateChandles adding Pair: {pairString}, Interval: {intervalId}, TimeStart: {timeFinish}, PriceStart: {candlesLast.PriceFinish}");

                IntervalData[intervalId][pairString].Add(new TradeHistoryCandleItem
                {
                    TimeStart = timeStart,
                    TimeFinish = timeFinish,
                    PriceStart = candlesLast.PriceFinish,
                    PriceFinish = candlesLast.PriceFinish,
                    PriceHigh = candlesLast.PriceFinish,
                    PriceLow = candlesLast.PriceFinish,
                    Volume = 0,
                    IsUpdated = true
                });

                timeStart = timeFinish;
            }

            var removeCount = IntervalData[intervalId][pairString].Count - length;
            if (0 < removeCount) IntervalData[intervalId][pairString].RemoveRange(0, removeCount);

            LogItemCount();
        }

        private static TradeHistoryCandleItem ComputeCandle(DatabaseContext dbContext, AssetPair assetPair, DateTime timeStart, DateTime timeFinish, decimal previousPrice)
        {
            var data = (assetPair.IsInternal) ? ComputeInternalCandle(dbContext, assetPair, timeStart, timeFinish) : ComputeExternalCandle(dbContext, assetPair, timeStart, timeFinish);

            var result = new TradeHistoryCandleItem();

            result.TimeStart = timeStart;
            result.TimeFinish = timeFinish;
            result.PriceStart = previousPrice;
            result.IsUpdated = false;

            // This means there were no transactions in the time period
            if (data == null)
            {
                result.PriceFinish = previousPrice;
                result.PriceHigh = previousPrice;
                result.PriceLow = previousPrice;
                result.Volume = 0;
            }
            else
            {
                result.PriceFinish = (decimal)data["PriceFinish"];
                result.PriceHigh = (decimal)data["PriceHigh"];
                result.PriceLow = (decimal)data["PriceLow"];
                result.Volume = (decimal)data["Volume"];
            }

            result.Volume *= (assetPair.IsInternal) ? VOLUME_FACTOR_INTERNAL : VOLUME_FACTOR_EXTERNAL;

            return result;
        }

        private static decimal FindPriceStart(DatabaseContext dbContext, AssetPair assetPair, DateTime timeStart, DateTime timeFinish)
        {
            decimal? priceStart;

            priceStart = FindPreviousPrice(dbContext, assetPair, timeStart);
            if (priceStart != null) return (decimal)priceStart;

            priceStart = FindPreviousPrice(dbContext, assetPair, timeFinish);
            if (priceStart != null) return (decimal)priceStart;

            if (assetPair.Price != null) return (decimal)assetPair.Price;

            throw new ApplicationException($"No starting price found for asset pair id {assetPair.Id}.");
        }

        private static decimal? FindPreviousPrice(DatabaseContext dbContext, AssetPair assetPair, DateTime timeFinish)
        {
            Dictionary<string, object> dataLast;
            decimal result;

            if (assetPair.IsInternal)
            {
                var query = $"SELECT TradeFill.Price"
                    + $" FROM TradeFill, Trade"
                    + $" WHERE TradeFill.TradeOneId = Trade.Id AND Trade.AssetPairId = {assetPair.Id}"
                    + $" AND TradeFill.TimeAdded < '{timeFinish.ToString("yyyy-MM-dd HH:mm:ss")}'"
                    + $" ORDER BY TradeFill.TimeAdded DESC, TradeFill.Id DESC";

                dataLast = dbContext.SelectOne(query);
                if (dataLast == null) return null;
                result = (decimal)dataLast["Price"];
            }
            else
            {
                if (assetPair.Exchange == null) throw new ApplicationException($"Exchange not set for external pair {assetPair.AssetOne.Ticker}-{assetPair.AssetTwo.Ticker}.");
                var exchangeAssetPair = dbContext.ExchangeAssetPair.FirstOrDefault(a => a.Exchange == assetPair.Exchange && a.AssetPair == assetPair && a.Status == Database.STATUS_ACTIVE);
                if (exchangeAssetPair == null) throw new ApplicationException($"Exchange pair not found for asset pair {assetPair.AssetOne.Ticker}-{assetPair.AssetTwo.Ticker}.");

/*                var query = $"SELECT Price"
                            + $" FROM ExchangeTradeHistory"
                            + $" WHERE ExchangeAssetPairId = {exchangeAssetPair.Id}"
                            + $" AND TimeExecuted < '{timeFinish.ToString("yyyy-MM-dd HH:mm:ss")}'"
                            + $" ORDER BY TimeExecuted DESC, Id DESC";

                dataLast = dbContext.SelectOne(query);
*/
                // new version
                var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;
                if (!TradeHistory.TradeFillData.ContainsKey(pairString)) throw new ApplicationException($"TradeHistoryChart.FindPreviousPrice: Ex Trade History asset pair not found for asset pair {assetPair.AssetOne.Code}-{assetPair.AssetTwo.Code}.");

                var itemResult = TradeHistory.TradeFillData[pairString].Where(x=> x.TimeExecuted < timeFinish).OrderByDescending(x => x.TimeExecuted).FirstOrDefault();
                if(itemResult == null) return null;
                result = itemResult.Price;
            }

            return result;
        }

        private static Dictionary<string, object> ComputeInternalCandle(DatabaseContext dbContext, AssetPair assetPair, DateTime timeStart, DateTime timeFinish)
        {
            var query = $"SELECT TradeFill.Price"
                        + $" FROM TradeFill, Trade"
                        + $" WHERE TradeFill.TradeOneId = Trade.Id AND Trade.AssetPairId = {assetPair.Id}"
                        + $" AND '{timeStart.ToString("yyyy-MM-dd HH:mm:ss")}' <= TradeFill.TimeAdded AND TradeFill.TimeAdded < '{timeFinish.ToString("yyyy-MM-dd HH:mm:ss")}'"
                        + $" ORDER BY TradeFill.TimeAdded DESC, TradeFill.Id DESC";

            var dataLast = dbContext.SelectOne(query);

            // This means there were no transactions in the time period
            if (dataLast == null) return null;

            query = $"SELECT MIN(TradeFill.Price) AS PriceLow, MAX(TradeFill.Price) AS PriceHigh, SUM(TradeFill.Quantity) AS Volume"
                        + $" FROM TradeFill, Trade"
                        + $" WHERE TradeFill.TradeOneId = Trade.Id AND Trade.AssetPairId = {assetPair.Id}"
                        + $" AND '{timeStart.ToString("yyyy-MM-dd HH:mm:ss")}' <= TradeFill.TimeAdded AND TradeFill.TimeAdded < '{timeFinish.ToString("yyyy-MM-dd HH:mm:ss")}'";

            var dataMinMax = dbContext.SelectOne(query);

            dataMinMax.Add("PriceFinish", dataLast["Price"]);

            return dataMinMax;
        }

        private static Dictionary<string, object> ComputeExternalCandle(DatabaseContext dbContext, AssetPair assetPair, DateTime timeStart, DateTime timeFinish)
        {
            if (assetPair.Exchange == null) throw new ApplicationException($"Exchange not set for external pair {assetPair.AssetOne.Ticker}-{assetPair.AssetTwo.Ticker}.");
            var exchangeAssetPair = dbContext.ExchangeAssetPair.FirstOrDefault(a => a.Exchange == assetPair.Exchange && a.AssetPair == assetPair && a.Status == Database.STATUS_ACTIVE);
            if (exchangeAssetPair == null) throw new ApplicationException($"Exchange pair not found for asset pair {assetPair.AssetOne.Ticker}-{assetPair.AssetTwo.Ticker}.");
/*
            var query = $"SELECT Price"
                        + $" FROM ExchangeTradeHistory"
                        + $" WHERE ExchangeAssetPairId = {exchangeAssetPair.Id}"
                        + $" AND '{timeStart.ToString("yyyy-MM-dd HH:mm:ss")}' <= TimeExecuted AND TimeExecuted < '{timeFinish.ToString("yyyy-MM-dd HH:mm:ss")}'"
                        + $" ORDER BY TimeExecuted DESC, Id DESC LIMIT 1";

            var dataLast = dbContext.SelectOne(query);

            // This means there were no transactions in the time period
            if (dataLast == null) return null;

            query = $"SELECT MIN(Price) AS PriceLow, MAX(Price) AS PriceHigh, SUM(Quantity) AS Volume"
                        + $" FROM ExchangeTradeHistory"
                        + $" WHERE ExchangeAssetPairId = {exchangeAssetPair.Id}"
                        + $" AND '{timeStart.ToString("yyyy-MM-dd HH:mm:ss")}' <= TimeExecuted AND TimeExecuted < '{timeFinish.ToString("yyyy-MM-dd HH:mm:ss")}'";

            var dataMinMax = dbContext.SelectOne(query);

            dataMinMax.Add("PriceFinish", dataLast["Price"]);
*/

            // new version
            Dictionary<string, object> dataMinMax = new Dictionary<string, object>();
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;
            if (!TradeHistory.TradeFillData.ContainsKey(pairString)) return null;//throw new ApplicationException($"TradeHistoryCharts.ComputeExternalCandle: Ex Trade History asset pair not found for asset pair {assetPair.AssetOne.Code}-{assetPair.AssetTwo.Code}.");

            var finishPrice = TradeHistory.TradeFillData[pairString].Where(x=> x.TimeExecuted >= timeStart && x.TimeExecuted < timeFinish).OrderByDescending(x => x.TimeExecuted).FirstOrDefault();
            if (finishPrice == null) return null;

            var minPrice = TradeHistory.TradeFillData[pairString].Where(x => x.TimeExecuted >= timeStart && x.TimeExecuted < timeFinish).Min(x=>x.Price);
            if (minPrice == 0) return null;

            var maxPrice = TradeHistory.TradeFillData[pairString].Where(x => x.TimeExecuted >= timeStart && x.TimeExecuted < timeFinish).Max(x => x.Price);
            if (maxPrice == 0) return null;

            var volume = TradeHistory.TradeFillData[pairString].Where(x => x.TimeExecuted >= timeStart && x.TimeExecuted < timeFinish).Sum(x => x.Quantity);
            if (volume == 0) return null;

            dataMinMax.Add("PriceFinish", finishPrice.Price);
            dataMinMax.Add("PriceLow", minPrice);
            dataMinMax.Add("PriceHigh", maxPrice);
            dataMinMax.Add("Volume", volume);

            return dataMinMax;
        }

        private static DateTime[] ComputeIntervalStarts(DatabaseContext dbContext, AssetPair assetPair, int intervalId)
        {
            DateTime timeLast;
            var dateTimes = new List<DateTime>();

            // Find the last TimeStart
            var lastCandle = dbContext.TradeHistoryCandle.Where(a => a.AssetPairId == assetPair.Id && a.IntervalId == intervalId).OrderByDescending(a => a.TimeStart).FirstOrDefault();

            if (lastCandle != null) timeLast = lastCandle.TimeStart;
            else timeLast = DateTime.Now;

            if (assetPair.Price == null) return dateTimes.ToArray();

            var length = IntervalTypes[intervalId].Length;
            var step = IntervalTypes[intervalId].Step;

            var timeFirst = timeLast.AddSeconds(-step * (length - 1));

            timeFirst = RoundStartTime(timeFirst, step);

            for (var i = 0; i < length; i++)
            {
                dateTimes.Add(timeFirst);
                timeFirst = timeFirst.AddSeconds(step);
                if (DateTime.Now < timeFirst) break;
            }

            return dateTimes.ToArray();
        }

        private static DateTime RoundStartTime(DateTime time, int step)
        {
            if (step <= 60) time = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0);
            else if (step <= 60 * 60) time = new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0);
            else time = new DateTime(time.Year, time.Month, time.Day, 0, 0, 0);

            return time;
        }

        private static int GetTotalItemCount()
        {
            var count = 0;

            for (var i = 0; i < IntervalData.Length; i++)
                foreach (var interval in IntervalData[i])
                    count += interval.Value.Count;

            return count;
        }

        private static void LogItemCount()
        {
            return;

            var count = GetTotalItemCount();

            Log.Information($"TradeHistoryChart, count = {count}. Memory used:{Decimal.Round(GC.GetTotalMemory(false)/1024,2)}kb");
        }
    }
}