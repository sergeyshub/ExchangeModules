using System;

namespace DataModels
{
    public class TradeHistoryItem
    {
        public DateTime TimeExecuted { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public bool IsUpdated { get; set; }
    }
}