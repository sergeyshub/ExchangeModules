namespace DataModels
{
    public class QuoteHistoryItem
    {
        public string PairString { get; set; }
        public decimal PriceStart { get; set; }
        public decimal PriceFinish { get; set; }
        public decimal PriceHigh { get; set; }
        public decimal PriceLow { get; set; }
        public decimal Volume { get; set; }
        public bool IsBook { get; set; }
        public bool IsShow { get; set; }
        public bool IsUpdated { get; set; }
    }
}
