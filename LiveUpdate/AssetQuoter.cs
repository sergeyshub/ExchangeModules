using System;
using System.Collections.Generic;
using DataAccess;
using ShortestPathLib;
using DataModels;
using HelpersLib;

namespace LiveUpdate
{
    public static class AssetQuoter
    {
        public static decimal? Get(Asset assetOne, Asset assetTwo)
        {
            return Get(assetOne.Code + "-" + assetTwo.Code);
        }

        public static decimal? Get(string pairString)
        {
            var codes = TextEx.ParsePairString(pairString);

            if (codes[0] == codes[1]) return 1;

            var price = FindPrice(pairString);

            return price;
        }

        public static decimal? GetChange(Asset assetOne, Asset assetTwo)
        {
            var pairString = assetOne.Code + "-" + assetTwo.Code;

            var codes = TextEx.ParsePairString(pairString);

            if (codes[0] == codes[1]) return 0;

            var change = FindPriceChange(pairString);

            return change;
        }

        private static decimal? FindPrice(string pairString)
        {
            if (!UpdateProcessor.IsInitialized) return null;

            var quotes = TradeQuotes.QuoteData;
            var pairPrices = new Dictionary<string, decimal>();

            foreach (var quote in quotes) pairPrices.Add(quote.Key, quote.Value.PriceFinish);

            var route = ShortestPath.FindRoute(pairString, pairPrices);
            if (route == null) return null;

            decimal price = 1;

            foreach (var pair in route) price *= pair.Price;

            return price;
        }

        private static decimal? FindPriceChange(string pairString)
        {
            if (!UpdateProcessor.IsInitialized) return null;

            var quotes = TradeQuotes.QuoteData;
            var pairPrices = new Dictionary<string, decimal>();

            foreach (var quote in quotes) pairPrices.Add(quote.Key, quote.Value.PriceFinish);

            var route = ShortestPath.FindRoute(pairString, pairPrices);
            if (route == null) return null;

            decimal change = 1;

            foreach (var pair in route)
            {
                bool isReversed = false;
                QuoteHistoryItem quote;

                if (TradeQuotes.QuoteData.ContainsKey(pair.Route))
                {
                    quote = TradeQuotes.QuoteData[pair.Route];
                }
                else
                {
                    isReversed = true;
                    var reversed = ReversePairString(pair.Route);

                    if (TradeQuotes.QuoteData.ContainsKey(reversed))
                        quote = TradeQuotes.QuoteData[reversed];
                    else
                        throw new ApplicationException($"A quote for {pair.Route} was not found.");
                }

                var pairChange = (quote.PriceStart == 0) ? 0 : (quote.PriceFinish - quote.PriceStart) / quote.PriceStart;

                if (isReversed) pairChange = -pairChange;

                change *= 1 + pairChange;
            }

            return Math.Round(change - 1, 2);
        }

        private static string ReversePairString(string pairString)
        {
            var words = TextEx.ParsePairString(pairString);
            return words[1] + "-" + words[0];
        }
    }
}
