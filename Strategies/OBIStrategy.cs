using System;
using System.Collections.Generic;
using System.Linq;
using NYCAlphaTrader.Core;
using TradingSystem.Configuration;

namespace NYCAlphaTrader.Strategies
{
    /// <summary>
    /// Order Book Imbalance (OBI) Strategy
    /// Predicts short-term price moves (10-500ms) by analyzing buy/sell pressure
    /// Expected: $200-400/day on $47k
    /// </summary>
    public class OBIStrategy
    {
        private readonly TradingConfig _config;
        private int _totalSignals = 0;
        private int _winningTrades = 0;
        private double _totalPnL = 0;
        private readonly StrategyParameterManager _paramManager;
        private OBIParameters _params;

        public OBIStrategy(TradingConfig config, StrategyParameterManager paramManager)
        {
            _config = config;
            _paramManager = paramManager;
            _params = paramManager.GetParameters<OBIParameters>();
        }

        public TradingSignal Analyze(MarketData market, MarketRegime regime)
        {
            if (!_config.EnableOBI)
                return null;

            // OBI works in all regimes but reduce size in high volatility
            double sizeMultiplier = 1.0;
            if (regime == MarketRegime.HighVolatility)
            {
                Console.WriteLine("[OBI] ⚠ High volatility - reducing size 50%");
                sizeMultiplier = 0.5;
            }

            // Calculate bid/ask volume imbalance in top N levels
            double bidVolume = market.BidLevels.Take(_config.ObiNumLevels).Sum(l => l.Quantity);
            double askVolume = market.AskLevels.Take(_config.ObiNumLevels).Sum(l => l.Quantity);
            double totalVolume = bidVolume + askVolume;

            // Imbalance ratio: -1 (all asks) to +1 (all bids)
            if (totalVolume < 0.01) // Minimum volume check
                return null;

            double imbalance = (bidVolume - askVolume) / totalVolume;
            double absImbalance = Math.Abs(imbalance);

            // Filter: spread must be reasonable
            // Replace hardcoded thresholds
            if (market.SpreadBps > _params.MaxSpreadBps)
                return null;

            if (absImbalance < _params.MinImbalanceThreshold)
                return null;

            if (totalVolume < _params.MinTotalVolume)
                return null;

            // Generate signal
            _totalSignals++;

            var signal = new TradingSignal
            {
                Symbol = market.Symbol,
                Strategy = "OBI",
                GeneratedAt = DateTime.UtcNow,
                Confidence = Math.Min(absImbalance / 0.7, 1.0)
            };

            // Strong bid volume → predict price UP
            if (imbalance > _config.ObiThreshold)
            {
                signal.Side = Side.Buy;
                signal.Price = market.BestAsk; // Cross spread
                signal.TargetPrice = market.MidPrice * (1.0 + 0.001); // 10 bps
                signal.StopPrice = market.MidPrice * (1.0 - 0.0005); // 5 bps
            }
            // Strong ask volume → predict price DOWN
            else if (imbalance < -_config.ObiThreshold)
            {
                signal.Side = Side.Sell;
                signal.Price = market.BestBid; // Cross spread
                signal.TargetPrice = market.MidPrice * (1.0 - 0.001); // 10 bps
                signal.StopPrice = market.MidPrice * (1.0 + 0.0005); // 5 bps
            }

            signal.Quantity = CalculateQuantity(market.MidPrice);
            signal.Metadata["imbalance"] = imbalance;
            signal.Metadata["bidVolume"] = bidVolume;
            signal.Metadata["askVolume"] = askVolume;

            // Apply regime-based size adjustment
            signal.Confidence *= sizeMultiplier;
            signal.Quantity *= sizeMultiplier;

            return signal;
        }

        public void RefreshParameters()
        {
            _params = _paramManager.GetParameters<OBIParameters>();
        }

        private double CalculateQuantity(double price)
        {
            // Use $3000 notional for OBI (smaller, more frequent)
            return 3000.0 / price;
        }

        public void RecordTradeResult(bool isWin, double pnl)
        {
            if (isWin) _winningTrades++;
            _totalPnL += pnl;
        }

        public (int signals, double winRate, double pnl) GetStats()
        {
            var winRate = _totalSignals > 0 ? (double)_winningTrades / _totalSignals : 0;
            return (_totalSignals, winRate, _totalPnL);
        }
    }
}
