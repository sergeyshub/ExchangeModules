using System;
using DataAccess;
using ExternalExchange.Binance;
using ExternalExchange.BinanceUs;
using ExternalExchange.Water;

namespace ExternalExchange
{
    public class ExternalExchangeFactory
    {
        protected DatabaseContext DbContext;

        public ExternalExchangeFactory(DatabaseContext dbContext)
        {
            DbContext = dbContext;
        }

        public ProtoExternalExchange CreateExchangeForPair(AssetPair assetPair)
        {
            if (assetPair.Exchange == null) DbContext.Entry(assetPair).Reference(a => a.Exchange).Load();

            switch (assetPair.Exchange.NameShort)
            {
            }
        }
    }
}
