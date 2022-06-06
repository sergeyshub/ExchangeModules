namespace DataModels
{
    public class OrderBookChartColumn
    {
        public decimal PriceStart { get; set; }
        public decimal PriceFinish { get; set; }
        public decimal Quantity { get; set; }
        public bool IsUpdated { get; set; }
    }
}
