using System;

namespace DataModels
{
    public class TradeHistoryCandleItem
    {
        public DateTime TimeStart { get; set; }
        public DateTime TimeFinish { get; set; }
        public decimal PriceStart { get; set; }
        public decimal PriceFinish { get; set; }
        public decimal PriceHigh { get; set; }
        public decimal PriceLow { get; set; }
        public decimal Volume { get; set; }
        public bool IsUpdated { get; set; }
    }
}