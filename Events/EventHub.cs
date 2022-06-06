using System;
using System.Collections.Generic;
using DataAccess;

namespace Events
{
    public static class EventHub
    {
        public static List<Action<DatabaseContext, Transaction>> FundingListeners;
        public static List<Action<DatabaseContext, TradeFill>> TradeFillListeners;
        public static List<Action<DatabaseContext, AssetPair, decimal, decimal, DateTime>> TradeHistoryListeners;

        static EventHub()
        {
            FundingListeners = new List<Action<DatabaseContext, Transaction>>();
            TradeFillListeners = new List<Action<DatabaseContext, TradeFill>>();
            TradeHistoryListeners = new List<Action<DatabaseContext, AssetPair, decimal, decimal, DateTime>>();
        }

        public static void AddFundingListener(Action<DatabaseContext, Transaction> listener)
        {
            FundingListeners.Add(listener);
        }

        // This is called when a transaction is confirmed or completed
        public static void TriggerFunding(DatabaseContext dbContext, Transaction transaction)
        {
            foreach (var listener in FundingListeners) listener(dbContext, transaction);
        }

        public static void AddTradeFillListener(Action<DatabaseContext, TradeFill> listener)
        {
            TradeFillListeners.Add(listener);
        }

        public static void TriggerTradeFill(DatabaseContext dbContext, TradeFill tradeFill)
        {
            foreach (var listener in TradeFillListeners) listener(dbContext, tradeFill);
        }

        public static void AddTradeHistoryListener(Action<DatabaseContext, AssetPair, decimal, decimal, DateTime> listener)
        {
            TradeHistoryListeners.Add(listener);
        }

        public static void TriggerTradeHistory(DatabaseContext dbContext, AssetPair assetPair, decimal price, decimal quantity, DateTime timeExecuted)
        {
            foreach (var listener in TradeHistoryListeners) listener(dbContext, assetPair, price, quantity, timeExecuted);
        }
    }
}
