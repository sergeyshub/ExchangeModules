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
    public static class OrderBookChart
    {
        public static Dictionary<string, List<OrderBookChartColumn>> Bids = null, RemovedBids = null;
        public static Dictionary<string, List<OrderBookChartColumn>> Asks = null, RemovedAsks = null;

        private class OrderBookChartLimits
        {
            public decimal PriceStart = 0, PriceFinish = 1000000000;
        }

        private static Dictionary<string, OrderBookChartLimits> OrderBookChartPairLimits;

        private const int COLUMN_NUMBER = 100;
        private const decimal RANGE_LIMIT = 0.1m;
        private const decimal RANGE_LIMIT_CHANGE = 0.01m;

        private static bool IsInitilized = false;

        private static Dictionary<string, SemaphoreSlim> semaphores;

        private static int DepthItemCount = 0;

        public static void Initialize(DatabaseContext dbContext)
        {
            var assetPairs = dbContext.AssetPair.Where(a => a.Status == Database.STATUS_ACTIVE && (a.IsInternal || a.Exchange.IsBook)).Include("AssetOne").Include("AssetTwo").ToList();

            Bids = new Dictionary<string, List<OrderBookChartColumn>>();
            Asks = new Dictionary<string, List<OrderBookChartColumn>>();
            RemovedBids = new Dictionary<string, List<OrderBookChartColumn>>();
            RemovedAsks = new Dictionary<string, List<OrderBookChartColumn>>();

            OrderBookChartPairLimits = new Dictionary<string, OrderBookChartLimits>();

            semaphores = new Dictionary<string, SemaphoreSlim>();

            foreach (var assetPair in assetPairs)
            {
                var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

                Bids.Add(pairString, new List<OrderBookChartColumn>());
                Asks.Add(pairString, new List<OrderBookChartColumn>());
                RemovedBids.Add(pairString, new List<OrderBookChartColumn>());
                RemovedAsks.Add(pairString, new List<OrderBookChartColumn>());

                OrderBookChartPairLimits.Add(pairString, new OrderBookChartLimits());

                semaphores.Add(pairString, new SemaphoreSlim(1));

                if (assetPair.IsInternal) InitializeInternalAssetPair(dbContext, assetPair);
                else InitializeExternalAssetPair(dbContext, assetPair);
            }

            IsInitilized = true;
        }

        public static async void AddDepth(DatabaseContext dbContext, AssetPair assetPair, bool isBuy, decimal price, decimal quantity)
        {
            if (!IsInitilized || quantity == 0) return;

            if (assetPair.AssetOne == null) dbContext.Entry(assetPair).Reference("AssetOne").Load();
            if (assetPair.AssetTwo == null) dbContext.Entry(assetPair).Reference("AssetTwo").Load();

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            if (!Bids.ContainsKey(pairString) || !Asks.ContainsKey(pairString)) return;

            await semaphores[pairString].WaitAsync();

            int i = 0;

            var columns = (isBuy) ? Bids[pairString] : Asks[pairString];

            try
            {
                if (price <= OrderBookChartPairLimits[pairString].PriceStart || OrderBookChartPairLimits[pairString].PriceFinish <= price) return;

                for (i = 0; i < columns.Count; i++)
                {
                    if ((isBuy && columns[i].PriceFinish < price && price <= columns[i].PriceStart)
                        || (!isBuy && columns[i].PriceStart <= price && price < columns[i].PriceFinish))
                    {
                        columns[i].Quantity = Math.Max(columns[i].Quantity + quantity, 0);
                        columns[i].IsUpdated = true;

                        if (columns[i].Quantity == 0) await RemoveColumn(assetPair, columns, isBuy, i);

                        await BalanceOrderBook(assetPair);

                        return;
                    }
                }

                AddNewColumn(assetPair, columns, isBuy, price, quantity);

                await BalanceOrderBook(assetPair);
            }
            catch (Exception e)
            {
                Log.Error($"OrderBookChart.AddDepth failed. Error: \"{e.Message}\". pairString = {pairString}, i = {i}, count = {columns.Count}, lastMethod = {lastMethod}");
            }
            finally
            {
                // Finally block is executed even after return;
                LogItemCount();

                await IntegrityCheck(assetPair, columns, isBuy, price, quantity);

                // Log.Information($"OrderBookChart.AddDepth finished, count = {columns.Count}");

                semaphores[pairString].Release();
            }
        }

        public static async Task<Dictionary<string, List<OrderBookChartColumn>>> GetAllColumns(string pairString)
        {
            var result = new Dictionary<string, List<OrderBookChartColumn>>();

            if (!IsInitilized)
            {
                result.Add("Bids", new List<OrderBookChartColumn>());
                result.Add("Asks", new List<OrderBookChartColumn>());

                return result;
            }

            await semaphores[pairString].WaitAsync();

            try
            {
                var bids = Bids[pairString].GetRange(0, Bids[pairString].Count);
                var asks = Asks[pairString].GetRange(0, Asks[pairString].Count);

                result.Add("Bids", bids);
                result.Add("Asks", asks);
            }
            finally
            {
                semaphores[pairString].Release();
            }

            return result;
        }

        public static async Task<Dictionary<string, List<OrderBookChartColumn>>> GetUpdatedColumns(string pairString)
        {
            var result = new Dictionary<string, List<OrderBookChartColumn>>();

            if (!IsInitilized)
            {
                result.Add("Bids", new List<OrderBookChartColumn>());
                result.Add("Asks", new List<OrderBookChartColumn>());

                return result;
            }

            await semaphores[pairString].WaitAsync();

            try
            {
                var bids = GetUpdatedBidAskColumns(pairString, true);
                var asks = GetUpdatedBidAskColumns(pairString, false);

                result.Add("Bids", bids);
                result.Add("Asks", asks);
            }
            finally
            {
                semaphores[pairString].Release();
            }

            return result;
        }

        private static List<OrderBookChartColumn> GetUpdatedBidAskColumns(string pairString, bool isBuy)
        {
            List<OrderBookChartColumn> resultColumns;

            var columns = (isBuy) ? Bids[pairString] : Asks[pairString];
            var removedColumns = (isBuy) ? RemovedBids[pairString] : RemovedAsks[pairString];

            resultColumns = columns.FindAll(x => x.IsUpdated).Select(c => { c.IsUpdated = false; return c; }).ToList();
            resultColumns.AddRange(removedColumns);

            removedColumns.Clear();

            return resultColumns;
        }

        private static void AddNewColumn(AssetPair assetPair, List<OrderBookChartColumn> columns, bool isBuy, decimal price, decimal quantity)
        {
            if (quantity <= 0) return;

            decimal priceStart = 0, priceFinish = 0;

            ComputeNewColumnPriceRange(assetPair, columns, isBuy, price, quantity, ref priceStart, ref priceFinish);

            var newColumn = new OrderBookChartColumn()
            {
                PriceStart = priceStart,
                PriceFinish = priceFinish,
                Quantity = quantity,
                IsUpdated = true
            };

            if (columns.Count == 0)
            {
                columns.Add(newColumn);

                return;
            }

            if (isBuy)
            {
                if (priceFinish < columns[0].PriceFinish)
                {
                    columns.Add(newColumn);
                }
                else
                {
                    columns.Insert(0, newColumn);
                }
            }
            else
            {
                if (priceStart < columns[0].PriceStart)
                {
                    columns.Insert(0, newColumn);
                }
                else
                {
                    columns.Add(newColumn);
                }
            }

            while (COLUMN_NUMBER < columns.Count) MergeTwoColumns(assetPair, columns, isBuy);
        }

        private static void MergeTwoColumns(AssetPair assetPair, List<OrderBookChartColumn> columns, bool isBuy)
        {
            if (columns.Count < 3) return; // never merge the first column

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            int index = 1;
            decimal minRange = Math.Abs(columns[index].PriceStart - Math.Abs(columns[index + 1].PriceFinish));

            for (var i = 2; i < columns.Count - 1; i++)
                if (Math.Abs(columns[i].PriceStart - Math.Abs(columns[i + 1].PriceFinish)) < minRange)
                {
                    minRange = Math.Abs(columns[i].PriceStart - Math.Abs(columns[i + 1].PriceFinish));
                    index = i;
                }

            // Log.Information($"OrderBookChart.MergeTwoColumns quantities: {columns[index].Quantity} and {columns[index + 1].Quantity}");

            columns[index].PriceFinish = columns[index + 1].PriceFinish;
            columns[index].Quantity += columns[index + 1].Quantity;
            columns[index].IsUpdated = true;

            columns[index + 1].Quantity = 0;
            columns[index + 1].IsUpdated = false;

            if (isBuy) RemovedBids[pairString].Add(columns[index + 1]);
            else RemovedAsks[pairString].Add(columns[index + 1]);

            columns.RemoveAt(index + 1);
        }

        private class ColumnData
        {
            public int Index;
            public decimal Range;
            public decimal Quantity;
        }

        public class ColumnDataComparer : IComparer<ColumnData>
        {
            int IComparer<ColumnData>.Compare(ColumnData columnOne, ColumnData columnTwo)
            {
                if (columnOne.Quantity < columnTwo.Quantity) return 1;
                if (columnOne.Quantity > columnTwo.Quantity) return -1;

                if (columnOne.Range < columnTwo.Range) return 1;
                if (columnOne.Range > columnTwo.Range) return -1;

                return 0;
            }
        }

        private static async Task SplitOneColumn(AssetPair assetPair, List<OrderBookChartColumn> columns, bool isBuy)
        {
            if (columns.Count < 1) return;

            ColumnData[] columnData = new ColumnData[columns.Count];

            for (var i = 0; i < columns.Count; i++)
                columnData[i] = new ColumnData()
                {
                    Index = i,
                    Range = Math.Abs(columns[i].PriceStart - columns[i].PriceFinish),
                    Quantity = columns[i].Quantity
                };

            Array.Sort(columnData, new ColumnDataComparer());

            var number = Math.Min(columns.Count, 5); // do not do more than 5 split attempts

            for (var i = 0; i < number; i++)
            {
                var task = TrySplittingAColumn(assetPair, columns, isBuy, columnData[i].Index);
                await task;

                if (task.Result) return;
            }
        }
        
        private static async Task<bool> TrySplittingAColumn(AssetPair assetPair, List<OrderBookChartColumn> columns, bool isBuy, int index)
        {
            Task<decimal> task;

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            decimal splitPrice = (columns[index].PriceStart + columns[index].PriceFinish) / 2;
            splitPrice = Math.Round(splitPrice, assetPair.AssetTwo.Decimals);

            if (splitPrice == columns[index].PriceStart || splitPrice == columns[index].PriceFinish)
            {
                // Log.Information($"OrderBookChart.TrySplittingAColumn the column is too narrow.");

                task = GetDepthQuantity(assetPair, isBuy, index);
                await task;
                var quantity = task.Result;
                if (quantity == 0) await RemoveColumn(assetPair, columns, isBuy, index, false);

                return true; // cannot split any more as the collection has changed
            }

            task = OrderBookDepth.GetQuantitySum(assetPair, isBuy, columns[index].PriceStart, splitPrice);
            await task;
            var quantityOne = task.Result;

            task = OrderBookDepth.GetQuantitySum(assetPair, isBuy, splitPrice, columns[index].PriceFinish);
            await task;
            var quantityTwo = task.Result;

            // This should never happen
            if (quantityOne == 0 && quantityTwo == 0)
            {
                // Log.Information($"OrderBookChart.TrySplittingAColumn both quantities are zero at index {index}");
                await RemoveColumn(assetPair, columns, isBuy, index, false);

                return true; // cannot split any more as the collection has changed
            }

            if (quantityOne == 0 && index == 0)
            {
                // Log.Information($"OrderBookChart.TrySplittingAColumn quantityOne is zero at index {index}");

                columns[index].PriceStart = splitPrice;
                columns[index].IsUpdated = true;

                return false;
            }

            if (quantityTwo == 0 && index == columns.Count - 1)
            {
                // Log.Information($"OrderBookChart.TrySplittingAColumn quantityTwo is zero at index {index}");

                columns[index].PriceFinish = splitPrice;
                columns[index].IsUpdated = true;

                return false;
            }

            if (quantityOne == 0 || quantityTwo == 0)
            {
                // Log.Information($"OrderBookChart.TrySplittingAColumn cannot split column at index {index}");

                return false;
            }

            var newColumn = new OrderBookChartColumn()
            {
                PriceStart = splitPrice,
                PriceFinish = columns[index].PriceFinish,
                Quantity = quantityTwo,
                IsUpdated = true
            };

            columns[index].PriceFinish = splitPrice;
            columns[index].Quantity = quantityOne;
            columns[index].IsUpdated = true;

            columns.Insert(index + 1, newColumn);

            // Log.Information($"OrderBookChart.TrySplittingAColumn success, quantities: {quantityOne} and {quantityTwo}");

            return true;
        }

        private static async Task RemoveColumn(AssetPair assetPair, List<OrderBookChartColumn> columns, bool isBuy, int index, bool isSplit = true)
        {
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            if (index != 0 && index != columns.Count - 1)
            {
                var price = (columns[index].PriceStart + columns[index].PriceFinish) / 2;
                price = Math.Round(price, assetPair.AssetTwo.Decimals);

                columns[index - 1].PriceFinish = price;
                columns[index + 1].PriceStart = price;
            }

            columns[index].Quantity = 0;
            columns[index].IsUpdated = false;

            if (isBuy) RemovedBids[pairString].Add(columns[index]);
            else RemovedAsks[pairString].Add(columns[index]);

            columns.RemoveAt(index);

            // Log.Information($"OrderBookChart.RemoveColumn {((isBuy) ? "Bids" : "Asks")}, index: {index}");

            if (!isSplit) return;

            if (columns.Count <= COLUMN_NUMBER) await SplitOneColumn(assetPair, columns, isBuy);
        }

        private static void ComputeNewColumnPriceRange(AssetPair assetPair, List<OrderBookChartColumn> columns, bool isBuy, decimal price, decimal quantity, ref decimal priceStart, ref decimal priceFinish)
        {
            if (columns.Count == 0)
            {
                var difference = (decimal)Math.Pow(10, -assetPair.AssetTwo.Decimals);
                priceStart = price;
                priceFinish = priceStart + ((isBuy) ? -difference : difference);

                if (priceStart == priceFinish) throw new ApplicationException("priceStart == priceFinish");

                return;
            }

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            decimal priceMin, priceMax, priceRange, step;

            if (isBuy)
            {
                priceMin = columns[columns.Count - 1].PriceFinish;
                priceMax = columns[0].PriceStart;
            }
            else
            {
                priceMin = columns[0].PriceStart;
                priceMax = columns[columns.Count - 1].PriceFinish;
            }

            priceMin = Math.Min(priceMin, price);
            priceMax = Math.Max(priceMax, price);

            priceRange = priceMax - priceMin;
            step = priceRange / (COLUMN_NUMBER - 1);
            step = Math.Round(step, assetPair.AssetTwo.Decimals);

            if (isBuy)
            {
                if (columns[0].PriceStart < price)
                {
                    if (Asks[pairString].Count == 0) priceStart = price + step / 2;
                    else if (Asks[pairString][0].PriceStart >= price + step / 2) priceStart = price + step / 2;
                    else if (Asks[pairString][0].PriceStart >= price) priceStart = Asks[pairString][0].PriceStart;
                    else priceStart = price;

                    priceFinish = columns[0].PriceStart;

                    if (priceStart == priceFinish) priceStart += (decimal)Math.Pow(10, -assetPair.AssetTwo.Decimals);
                }
                else if (price <= columns[columns.Count - 1].PriceFinish)
                {
                    priceStart = columns[columns.Count - 1].PriceFinish;
                    priceFinish = price - step / 2;

                    if (priceStart == priceFinish || price == priceFinish) priceFinish -= (decimal)Math.Pow(10, -assetPair.AssetTwo.Decimals);
                }
                else
                {
                    throw new ApplicationException("Price cannot be within the range of existing prices.");
                }
            }
            else
            {
                if (price < columns[0].PriceStart)
                {
                    if (Bids[pairString].Count == 0) priceStart = price - step / 2;
                    else if (Bids[pairString][0].PriceStart <= price - step / 2) priceStart = price - step / 2;
                    else if (Bids[pairString][0].PriceStart <= price) priceStart = Bids[pairString][0].PriceStart;
                    else priceStart = price;

                    priceFinish = columns[0].PriceStart;

                    if (priceStart == priceFinish) priceStart -= (decimal)Math.Pow(10, -assetPair.AssetTwo.Decimals);
                }
                else if (columns[columns.Count - 1].PriceFinish <= price)
                {
                    priceStart = columns[columns.Count - 1].PriceFinish;
                    priceFinish = price + step / 2;

                    if (priceStart == priceFinish || price == priceFinish) priceFinish += (decimal)Math.Pow(10, -assetPair.AssetTwo.Decimals);
                }
                else
                {
                    throw new ApplicationException("Price cannot be within the range of existing prices.");
                }
            }

            if (priceStart == priceFinish) throw new ApplicationException("priceStart == priceFinish");
            if (price == priceFinish) throw new ApplicationException("price == priceFinish");

            // Log.Information($"ComputeNewColumnPriceRange, added {price} to {((isBuy) ? "bids" : "asks")}, priceStart: {priceStart}, priceFinish: {priceFinish}");
        }

        public static async Task<decimal> GetDepthQuantity(AssetPair assetPair, bool isBuy, int index)
        {
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            var columns = (isBuy) ? Bids[pairString] : Asks[pairString];

            var task = OrderBookDepth.GetQuantitySum(assetPair, isBuy, columns[index].PriceStart, columns[index].PriceFinish);

            await task;

            var depthQuantity = task.Result;

            return depthQuantity;
        }

        private static void InitializeInternalAssetPair(DatabaseContext dbContext, AssetPair assetPair)
        {
            decimal? priceMin, priceMax;
            decimal priceRange, priceStart, step;

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            priceMin = dbContext.TradeOrder.Where(a => a.AssetPair == assetPair && a.IsBuy == true).Min(a => a.PriceLimit);
            priceMax = dbContext.TradeOrder.Where(a => a.AssetPair == assetPair && a.IsBuy == true).Max(a => a.PriceLimit);

            if (priceMin == null || priceMax == null) return;

            priceRange = (decimal)priceMax - (decimal)priceMin;
            step = priceRange / (COLUMN_NUMBER - 1);
            step = Math.Round(step, assetPair.AssetTwo.Decimals);

            priceStart = (decimal)priceMax + step / 2;

            for (var i = 0; i < COLUMN_NUMBER; i++)
            {
                var column = new OrderBookChartColumn()
                {
                    PriceStart = priceStart,
                    PriceFinish = priceStart - step,
                    IsUpdated = true
                };

                column.Quantity = dbContext.TradeOrder.Where(
                    a => a.AssetPair == assetPair
                    && a.IsBuy == true
                    && column.PriceFinish < a.PriceLimit && a.PriceLimit <= column.PriceStart)
                    .Sum(a => a.Quantity);

                Bids[pairString].Add(column);

                priceStart -= step;
            }

            priceMin = dbContext.TradeOrder.Where(a => a.AssetPair == assetPair && a.IsBuy == false).Min(a => a.PriceLimit);
            priceMax = dbContext.TradeOrder.Where(a => a.AssetPair == assetPair && a.IsBuy == false).Max(a => a.PriceLimit);

            if (priceMin == null || priceMax == null) return;

            priceRange = (decimal)priceMax - (decimal)priceMin;
            step = priceRange / (COLUMN_NUMBER - 1);

            priceStart = (decimal)priceMin - step / 2;

            for (var i = 0; i < COLUMN_NUMBER; i++)
            {
                var column = new OrderBookChartColumn()
                {
                    PriceStart = priceStart,
                    PriceFinish = priceStart + step,
                    IsUpdated = true
                };

                column.Quantity = dbContext.TradeOrder.Where(
                    a => a.AssetPair == assetPair
                    && a.IsBuy == true
                    && column.PriceStart <= a.PriceLimit && a.PriceLimit < column.PriceFinish)
                    .Sum(a => a.Quantity);

                Bids[pairString].Add(column);

                priceStart += step;
            }

            LogItemCount();
        }

        private static void InitializeExternalAssetPair(DatabaseContext dbContext, AssetPair assetPair)
        {
            // This does nothing, as when an external exchange started the OrderBook data is outdated and deleted
        }

        private static async Task BalanceOrderBook(AssetPair assetPair)
        {
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            if (Bids[pairString].Count == 0 || Asks[pairString].Count == 0) return;

            await AdjustPairRangeLimit(assetPair);

            var spread = Asks[pairString][0].PriceStart - Bids[pairString][0].PriceStart;
            if (spread < 0)
            {
                var depthSpread = GetDepthSpread(assetPair);

                if (spread < depthSpread)
                {
                    // Log.Warning($"OrderBookChart:BalanceOrderBook negative {pairString} spread: {spread}, depth spread: {depthSpread}");

                    await TrySplittingAColumn(assetPair, Bids[pairString], true, 0);
                    await TrySplittingAColumn(assetPair, Asks[pairString], false, 0);

                    while (COLUMN_NUMBER < Bids[pairString].Count) MergeTwoColumns(assetPair, Bids[pairString], true);
                    while (COLUMN_NUMBER < Asks[pairString].Count) MergeTwoColumns(assetPair, Asks[pairString], false);
                }
            }
        }

        private static async Task AdjustPairRangeLimit(AssetPair assetPair)
        {
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            if (Bids[pairString].Count == 0 || Asks[pairString].Count == 0) return;

            decimal priceStart = 0, priceFinish = 0;

            GetShowStartFinishPriceColumns(assetPair, ref priceStart, ref priceFinish);

            var isChange = false;

            var changeStart = Math.Abs(priceStart - OrderBookChartPairLimits[pairString].PriceStart) / priceStart;
            var changeFinish = Math.Abs(priceFinish - OrderBookChartPairLimits[pairString].PriceFinish) / priceFinish;

            if (RANGE_LIMIT_CHANGE < changeStart || RANGE_LIMIT_CHANGE < changeFinish) isChange = true;

            if (!isChange) return;

            // if (pairString == "btc-usdt") Log.Information($"OrderBookChart.AdjustPairRangeLimit old range: {OrderBookChartPairLimits[pairString].PriceStart}-{OrderBookChartPairLimits[pairString].PriceFinish}, new range: {priceStart}-{priceFinish}, {Math.Round(changeStart * 100, 2)}%");

            List<OrderBookChartColumn> columns;
            OrderBookChartColumn newColumn;
            decimal quantity;
            Task<decimal> task;

            columns = Bids[pairString];

            while (columns[columns.Count - 1].PriceStart < priceStart) await RemoveColumn(assetPair, columns, true, columns.Count - 1, false);

            if (columns.Count == 0) return;

            task = OrderBookDepth.GetQuantitySum(assetPair, true, priceStart, columns[columns.Count - 1].PriceFinish);
            await task;
            quantity = task.Result;

            if (0 < quantity)
            {
                newColumn = new OrderBookChartColumn()
                {
                    PriceStart = columns[columns.Count - 1].PriceFinish,
                    PriceFinish = priceStart,
                    Quantity = quantity,
                    IsUpdated = true
                };

                columns.Add(newColumn);
            }

            columns = Asks[pairString];

            while (priceFinish < columns[columns.Count - 1].PriceStart) await RemoveColumn(assetPair, columns, false, columns.Count - 1, false);

            if (columns.Count == 0) return;

            task = OrderBookDepth.GetQuantitySum(assetPair, false, columns[columns.Count - 1].PriceFinish, priceFinish);
            await task;
            quantity = task.Result;

            if (0 < quantity)
            {
                newColumn = new OrderBookChartColumn()
                {
                    PriceStart = columns[columns.Count - 1].PriceFinish,
                    PriceFinish = priceFinish,
                    Quantity = quantity,
                    IsUpdated = true
                };

                columns.Add(newColumn);
            }

            OrderBookChartPairLimits[pairString].PriceStart = priceStart;
            OrderBookChartPairLimits[pairString].PriceFinish = priceFinish;
        }

        private static void GetShowStartFinishPriceColumns(AssetPair assetPair, ref decimal priceStart, ref decimal priceFinish)
        {
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            var price = (Bids[pairString][0].PriceStart + Asks[pairString][0].PriceStart) / 2;

            priceStart = price * (1 - RANGE_LIMIT);
            priceFinish = price * (1 + RANGE_LIMIT);

            priceStart = Math.Round(priceStart, assetPair.AssetTwo.Decimals);
            priceFinish = Math.Round(priceFinish, assetPair.AssetTwo.Decimals);

            // Log.Information($"OrderBookChart.GetShowStartFinishPriceColumns price = {price}, startPrice = {priceStart}, finishPrice = {priceFinish}");
        }

        private static decimal GetDepthSpread(AssetPair assetPair)
        {
            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            if (OrderBookDepth.Asks[pairString].Count == 0 || OrderBookDepth.Bids[pairString].Count == 0
                || OrderBookDepth.Bids[pairString][0].Quantity == 0 || OrderBookDepth.Asks[pairString][0].Quantity == 0) return 0;

            return OrderBookDepth.Asks[pairString][0].Price - OrderBookDepth.Bids[pairString][0].Price;
        }

        private static async Task IntegrityCheck(AssetPair assetPair, List<OrderBookChartColumn> columns, bool isBuy, decimal price, decimal quantity)
        {
            return;

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            if (isBuy)
                for (var i = 0; i < columns.Count; i++)
                {
                    if (columns[i].PriceStart <= columns[i].PriceFinish) throw new ApplicationException($"Bids: PriceStart below PriceFinish at index {i}.");
                    if (i < columns.Count - 1 && columns[i].PriceFinish != columns[i + 1].PriceStart) throw new ApplicationException($"Bids: next PriceStart != PriceFinish at index {i}.");
                }
            else
                for (var i = 0; i < columns.Count; i++)
                {
                    if (columns[i].PriceFinish <= columns[i].PriceStart) throw new ApplicationException($"Asks: next PriceFinish below PriceStart at index {i}.");
                    if (i < columns.Count - 1 && columns[i].PriceFinish != columns[i + 1].PriceStart) throw new ApplicationException($"Asks: next PriceStart != PriceFinish at index {i}.");
                }

            if (columns.Count == 0) return;

            var task = GetDepthQuantity(assetPair, isBuy, 0);
            await task;
            var depthQuantity = task.Result;
            var difference = columns[0].Quantity - depthQuantity;
            if (difference != 0) Log.Warning($"Depth quantity difference: {difference}.");
        }

        private static int GetTotalItemCount()
        {
            var count = 0;

            foreach (var item in Bids) count += item.Value.Count;

            foreach (var item in Asks) count += item.Value.Count;

            return count;
        }

        private static void LogItemCount()
        {
            return;

            var count = GetTotalItemCount();

            var change = Math.Abs(DepthItemCount - count);

            if (count == 0 || (DepthItemCount != 0 && (double)change / (double)DepthItemCount < 0.50)) return;

            DepthItemCount = count;

            Log.Information($"OrderBookChart, count = {DepthItemCount} ");
        }
    }
}