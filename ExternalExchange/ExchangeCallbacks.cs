using System;

namespace ExternalExchange
{
    public class ExchangeCallbacks
    {
        public Action ProcessTradeFills = null;
        public Action ProcessTradeCancels = null;
        public Action<int, decimal> UpdateSpotRate = null;
        public Action<int, decimal, decimal, DateTime> AddTradeFill = null;
        public Action<int, bool, decimal, decimal, bool, long?> AddDepth = null;
        public Action<int, long?> DeleteDepths = null;
    }
}