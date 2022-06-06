namespace DataModels
{
    public class OrderBookItem
    {
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public bool IsUpdated { get; set; }
        public long UpdateId { get; set; }
    }
}
