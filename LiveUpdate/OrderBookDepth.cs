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
    public static class OrderBookDepth
    {
        public static Dictionary<string, List<OrderBookItem>> Bids = null;
        public static Dictionary<string, List<OrderBookItem>> Asks = null;

        private static bool IsInitilized = false;

        private static Dictionary<string, SemaphoreSlim> semaphores;

        private static int DepthItemCount = 0;
        private static DateTime DepthItemTime = DateTime.Now;
        private static DateTime DepthItemTime2 = DateTime.Now;

        public static void Initialize(DatabaseContext dbContext)
        {
            // Should be the same as in LiveUpdate:
            var assetPairs = dbContext.AssetPair.Where(a => a.Status == Database.STATUS_ACTIVE &&
                                                       (a.IsInternal || (a.Exchange.IsBook && a.Exchange.Status == Database.STATUS_ACTIVE)))
                                                .Include("AssetOne").Include("AssetTwo").ToList();

            Bids = new Dictionary<string, List<OrderBookItem>>();
            Asks = new Dictionary<string, List<OrderBookItem>>();

            semaphores = new Dictionary<string, SemaphoreSlim>();

            foreach (var assetPair in assetPairs)
            {
                var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

                Bids.Add(pairString, new List<OrderBookItem>());
                Asks.Add(pairString, new List<OrderBookItem>());

                semaphores.Add(pairString, new SemaphoreSlim(1));

                if (assetPair.IsInternal) InitializeInternalAssetPair(dbContext, assetPair);
                else InitializeExternalAssetPair(dbContext, assetPair);
            }

            IsInitilized = true;
        }

        public static async void AddDepth(DatabaseContext dbContext, AssetPair assetPair, bool isBuy, decimal price, decimal quantity, bool isDelta = true, long? updateId = null)
        {
            if (!IsInitilized) return;

            if (assetPair.AssetOne == null) dbContext.Entry(assetPair).Reference("AssetOne").Load();
            if (assetPair.AssetTwo == null) dbContext.Entry(assetPair).Reference("AssetTwo").Load();

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            // Log.Information($"OrderBookDepth.AddDepth pair: {pairString}, price: {price}, quanity: {quantity}, isDelta: {isDelta}");

            if (!Bids.ContainsKey(pairString) || !Asks.ContainsKey(pairString)) return;

            await semaphores[pairString].WaitAsync();

            int i = 0, nonZeroCount = 0;
            var depths = (isBuy) ? Bids[pairString] : Asks[pairString];

            try
            {
                if (depths.Count == 0)
                {
                    if (0 < quantity)
                    {
                        depths.Add(new OrderBookItem()
                        {
                            Price = price,
                            Quantity = quantity,
                            IsUpdated = true,
                            UpdateId = (updateId == null) ? 0 : (long)updateId
                        });

                        OrderBookChart.AddDepth(dbContext, assetPair, isBuy, price, quantity);
                    }

                    return;
                }

                while (i < depths.Count)
                {
                    if (price == depths[i].Price)
                    {
                        if (updateId == null || depths[i].UpdateId < updateId)
                        {
                            decimal quantityChange;

                            if (isDelta)
                            {
                                var oldQuantity = depths[i].Quantity;
                                depths[i].Quantity = Math.Max(depths[i].Quantity + quantity, 0);
                                quantityChange = depths[i].Quantity - oldQuantity;
                            }
                            else
                            {
                                quantityChange = quantity - depths[i].Quantity;
                                depths[i].Quantity = quantity;
                            }

                            depths[i].IsUpdated = true;

                            OrderBookChart.AddDepth(dbContext, assetPair, isBuy, price, quantityChange);

                            // Remove 0 fix
                            //if (depths[i].Quantity == 0) depths.Remove(depths[i]);

                        }

                        return;
                    }

                    if (Compare(isBuy, price, depths[i].Price))
                    {
                        if (0 < quantity)
                        {
                            depths.Insert(i, new OrderBookItem()
                            {
                                Price = price,
                                Quantity = quantity,
                                IsUpdated = true,
                                UpdateId = (updateId == null) ? 0 : (long)updateId
                            });

                            OrderBookChart.AddDepth(dbContext, assetPair, isBuy, price, quantity);
                        }

                        return;
                    }

                    if (0 < depths[i].Quantity) nonZeroCount++;

                    i++;
                }

                if (0 < quantity)
                {
                    depths.Add(new OrderBookItem()
                    {
                        Price = price,
                        Quantity = quantity,
                        IsUpdated = true,
                        UpdateId = (updateId == null) ? 0 : (long)updateId
                    });

                    OrderBookChart.AddDepth(dbContext, assetPair, isBuy, price, quantity);
                }
            }
            catch (Exception e)
            {
                Log.Error($"OrderBookDepth.AddDepth failed. Error: \"{e.Message}\". Details: \"{e.InnerException}\".");
            }
            finally
            {
                // Finally block is executed even after return;
                LogItemCount();

                /*
                if (pairString == "btc-usdt")
                {
                    if (0 < Bids[pairString].Count && 0 < Asks[pairString].Count && Bids[pairString][0].Quantity != 0 && Asks[pairString][0].Quantity != 0)
                    {
                        var spread = Asks[pairString][0].Price - Bids[pairString][0].Price;
                        if (spread < 0)
                        {
                            var sortedBids = DBids.OrderBy(kvp => kvp.Key).Last();
                            var sortedAsks = DAsks.OrderBy(kvp => kvp.Key).First();
                            var maxBid = sortedBids.Key;
                            var minAsk = sortedAsks.Key;
                            var dSpread = minAsk - maxBid;

                            var isTheSame = (spread == dSpread) ? "the same" : "different!";

                            Log.Warning($"Negative {pairString} spread: {spread}, bid/ask: {maxBid}/{minAsk} - {isTheSame}");
                        }
                        // else Log.Error($"OrderBookDepth.AddDepth {pairString} spread: {spread}");
                    }
                }
                */

                semaphores[pairString].Release();
            }
        }

        public static async void DeleteDepths(DatabaseContext dbContext, AssetPair assetPair, long? updateId = null)
        {
            if (!IsInitilized) return;

            if (assetPair.AssetOne == null) dbContext.Entry(assetPair).Reference("AssetOne").Load();
            if (assetPair.AssetTwo == null) dbContext.Entry(assetPair).Reference("AssetTwo").Load();

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            // Log.Information($"OrderBookDepth.DeleteDepths pair: {pairString}, updateId: {updateId}");

            if (!Bids.ContainsKey(pairString) || !Asks.ContainsKey(pairString)) return;

            await semaphores[pairString].WaitAsync();

            try
            {
                DeleteOrderDepths(dbContext, assetPair, Bids[pairString], true, updateId);
                DeleteOrderDepths(dbContext, assetPair, Asks[pairString], false, updateId);
            }
            catch (Exception e)
            {
                Log.Error($"OrderBookDepth.DeleteDepths failed. Error: \"{e.Message}\". Details: \"{e.InnerException}\".");
            }
            finally
            {
                // Finally block is executed even after return;

                LogItemCount();

                semaphores[pairString].Release();
            }
        }

        public static async Task<Dictionary<string, List<OrderBookItem>>> GetUpdatedDepths(string pairString)
        {
            var tStart = DateTime.Now;
            var result = new Dictionary<string, List<OrderBookItem>>();

            if (!IsInitilized)
            {
                result.Add("Bids", new List<OrderBookItem>());
                result.Add("Asks", new List<OrderBookItem>());

                return result;
            }

            await semaphores[pairString].WaitAsync();

            try
            {
                List<OrderBookItem> resultDepths;

                resultDepths = Bids[pairString].FindAll(x => x.IsUpdated).Select(c => { c.IsUpdated = false; return c; }).ToList();
                var r = Bids[pairString].RemoveAll(x => x.Quantity == 0);
                result.Add("Bids", resultDepths);

                resultDepths = Asks[pairString].FindAll(x => x.IsUpdated).Select(c => { c.IsUpdated = false; return c; }).ToList();
                r += Asks[pairString].RemoveAll(x => x.Quantity == 0);
                result.Add("Asks", resultDepths);
                if (pairString=="btc-usd" && DateTime.Now.Subtract(DepthItemTime2).TotalSeconds > 10)
                {
                    DepthItemTime2 = DateTime.Now;
                    // Log.Information($"GetUpdatedDepths {pairString} removed {r} items. Total Bids: {Bids[pairString].Count}. Total Asks: {Asks[pairString].Count}. Duration: {DateTime.Now.Subtract(tStart).TotalMilliseconds} ms");
                };
            }
            finally
            {
                semaphores[pairString].Release();
            }

            return result;
        }

        public static async Task<decimal> GetQuantitySum(AssetPair assetPair, bool isBuy, decimal priceStart, decimal priceFinish)
        {
            decimal depthQuantity;
            List<OrderBookItem> depths;

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            while (true)
            {
                await semaphores[pairString].WaitAsync();

                try
                {
                    if (isBuy)
                    {
                        depths = Bids[pairString];
                        depthQuantity = depths.Where(a => priceFinish < a.Price && a.Price <= priceStart).Sum(a => a.Quantity);
                    }
                    else
                    {
                        depths = Asks[pairString];
                        depthQuantity = depths.Where(a => priceStart <= a.Price && a.Price < priceFinish).Sum(a => a.Quantity);
                    }

                    return depthQuantity;
                }
                catch (Exception e)
                {
                    Log.Error($"OrderBookDepth.GetQuantitySum failed. Error: \"{e.Message}\".");
                }
                finally
                {
                    semaphores[pairString].Release();
                }
            }
        }

        private static void DeleteOrderDepths(DatabaseContext dbContext, AssetPair assetPair, List<OrderBookItem> depths, bool isBuy, long? updateId = null)
        {
            // var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            var i = 0;

            while (i < depths.Count)
            {
                if (depths[i].Quantity != 0 && (updateId == null || depths[i].UpdateId < updateId))
                {
                    var oldQuantity = depths[i].Quantity; 

                    depths[i].Quantity = 0;
                    depths[i].IsUpdated = true;

                    // Log.Information($"OrderBook.DeleteOrderDepths {pairString}, {((isBuy) ? "Bids" : "Asks")}, price: {depths[i].Price}, oldQuantity: {-oldQuantity}");

                    OrderBookChart.AddDepth(dbContext, assetPair, isBuy, depths[i].Price, -oldQuantity);

                    //Remove 0 fix
                    //depths.Remove(depths[i]);
                }

                i++;
            }
        }

        private static void InitializeInternalAssetPair(DatabaseContext dbContext, AssetPair assetPair)
        {
            string query;
            List<Dictionary<string, object>> depthItems;

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            query = $"SELECT PriceLimit, SUM(QuantityLeft) AS Depth FROM TradeOrder WHERE AssetPairId = {assetPair.Id} AND PriceLimit IS NOT NULL AND IsBuy = 1 GROUP BY PriceLimit ORDER BY PriceLimit DESC";

            depthItems = dbContext.Select(query);

            foreach (var item in depthItems)
                Bids[pairString].Add(new OrderBookItem()
                {
                    Price = (decimal)item["PriceLimit"],
                    Quantity = (decimal)item["Depth"],
                    IsUpdated = false
                });

            query = $"SELECT PriceLimit, SUM(QuantityLeft) AS Depth FROM TradeOrder WHERE AssetPairId = {assetPair.Id} AND PriceLimit IS NOT NULL AND IsBuy = 0 GROUP BY PriceLimit ORDER BY PriceLimit ASC";

            depthItems = dbContext.Select(query);

            foreach (var item in depthItems)
                Asks[pairString].Add(new OrderBookItem()
                {
                    Price = (decimal)item["PriceLimit"],
                    Quantity = (decimal)item["Depth"],
                    IsUpdated = false
                });

            LogItemCount();
        }

        private static void InitializeExternalAssetPair(DatabaseContext dbContext, AssetPair assetPair)
        {
            // This does nothing, as when an external exchange started the OrderBook data is outdated and deleted
        }

        private static bool Compare(bool isGreater, decimal value1, decimal value2)
        {
            return (isGreater) ? value1 > value2 : value1 < value2;
        }

        public class FillQuantities
        {
            public decimal QuantityAssetOne;
            public decimal QuantityAssetTwo;
        }

        public static FillQuantities ComputeAssetTwoQuantity(DatabaseContext dbContext, AssetPair assetPair, bool isBuy, decimal quantityAssetOne)
        {
            if (!IsInitilized) return null;

            if (assetPair.AssetOne == null) dbContext.Entry(assetPair).Reference("AssetOne").Load();
            if (assetPair.AssetTwo == null) dbContext.Entry(assetPair).Reference("AssetTwo").Load();

            var pairString = assetPair.AssetOne.Code + "-" + assetPair.AssetTwo.Code;

            var depths = (isBuy) ? Asks[pairString] : Bids[pairString];

            int i = 0;
            decimal quanityLeft = quantityAssetOne;
            decimal quantityAssetTwo = 0;

            while (i < depths.Count)
            {
                var quantityFilled = Math.Min(quanityLeft, depths[i].Quantity);
                quantityAssetTwo += quantityFilled * depths[i].Price;
                quanityLeft -= quantityFilled;

                if (quanityLeft == 0) break;
                i++;
            }

            return new FillQuantities{ QuantityAssetOne = quantityAssetOne - quanityLeft, QuantityAssetTwo = quantityAssetTwo };
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
            //return;

            if (DateTime.Now.Subtract(DepthItemTime).TotalMinutes < 30) return;
            DepthItemTime = DateTime.Now;

            var count = GetTotalItemCount();

            //var change = Math.Abs(DepthItemCount - count);

            //if (count == 0 || (DepthItemCount != 0 && (double)change / (double)DepthItemCount < 0.50)) return;
            //if (count == 0 || change<200) return;

            //DepthItemCount = count;

            Log.Information($"OrderBookDepth, count = {count}. Total Memory used:{Decimal.Round(GC.GetTotalMemory(false) / 1024, 2)}kb");
        }
    }
}