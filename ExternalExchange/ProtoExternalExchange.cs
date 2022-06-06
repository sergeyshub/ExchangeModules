using DataAccess;

namespace ExternalExchange
{
    public abstract class ProtoExternalExchange
    {
        public const int RESULT_SUCCESS = 0;
        public const int RESULT_ERROR = 1;
        public const int RESULT_ALREADY_COMPLETED = 2;

        public Exchange Exchange;
        protected DatabaseContext DbContext;

        public ProtoExternalExchange(DatabaseContext dbContext)
        {
            DbContext = dbContext;
        }

        public abstract void Initialize(ExchangeCallbacks externalExchangeConfig);

        public abstract bool IsInitialized();

        public abstract ExchangeAssetBalance GetSystemAssetBalance(Asset asset);

        public abstract ExchangeAssetBalance GetActualAssetBalance(Asset asset);

        public abstract ExchangeTrade AddOrder(Trade trade, decimal quantityReserved);

        public abstract void SendOrder(Trade trade);

        public abstract byte CancelOrder(Trade trade);

        public abstract ExchangeTradeFee ComputeTradeFee(AssetPair assetPair, bool isBuy, decimal quantity, decimal? priceLimit);

        public abstract decimal RoundDownQuantity(AssetPair assetPair, decimal quantity);
    }
}
