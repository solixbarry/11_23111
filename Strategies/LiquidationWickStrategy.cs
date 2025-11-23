using System;
using System.Collections.Generic;
using System.Linq;
using NYCAlphaTrader.Core;
using TradingSystem.Configuration;

namespace NYCAlphaTrader.Strategies
{
    /// <summary>
    /// Liquidation Wick Capture Strategy
    /// Catches forced liquidations that create sharp price wicks
    /// Expected: $40-120/day
    /// </summary>
    public class LiquidationWickStrategy
    {
        private readonly TradingConfig _config;
        private readonly List<PricePoint> _recentPrices = new List<PricePoint>();
        private double _volumeMA = 0;
        private int _totalSignals = 0;
        private double _totalPnL = 0;

        private const int PriceHistoryWindow = 50;
        private readonly StrategyParameterManager _paramManager;
        private LiquidationWickParameters _params;

        private class PricePoint
        {
            public double Price { get; set; }
            public double Volume { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public LiquidationWickStrategy(TradingConfig config, StrategyParameterManager paramManager)
        {
            _config = config;
            _paramManager = paramManager;
            _params = paramManager.GetParameters<LiquidationWickParameters>();
        }

        public void UpdatePrice(double price, double volume)
        {
            _recentPrices.Add(new PricePoint
            {
                Price = price,
                Volume = volume,
                Timestamp = DateTime.UtcNow
            });

            if (_recentPrices.Count > PriceHistoryWindow)
            {
                _recentPrices.RemoveAt(0);
            }

            // Update volume MA
            if (_recentPrices.Count >= 10)
            {
                _volumeMA = _recentPrices.TakeLast(10).Average(p => p.Volume);
            }
        }

        public TradingSignal Analyze(MarketData market, MarketRegime regime)
        {
            if (!_config.EnableLiquidationWicks)
                return null;

            // Liquidation wick INCREASES size in high volatility (more liquidations occur)
            double sizeMultiplier = 1.0;
            if (regime == MarketRegime.HighVolatility)
            {
                Console.WriteLine("[WICK] ↑ High volatility - INCREASING size 50%");
                sizeMultiplier = 1.5;
            }

            if (_recentPrices.Count < 10)
                return null;

            // Detect wick: sharp move in last few ticks
            var last10 = _recentPrices.TakeLast(10).ToList();
            double highPrice = last10.Max(p => p.Price);
            double lowPrice = last10.Min(p => p.Price);
            double avgPrice = last10.Average(p => p.Price);

            // Check for volume spike
            double currentVolume = market.Volume24h;
            double volumeSpike = currentVolume / _volumeMA;

            // Calculate wick size
            double wickUpPercent = (highPrice - avgPrice) / avgPrice;
            double wickDownPercent = (avgPrice - lowPrice) / avgPrice;

            double wickThreshold = _config.LiqWickSizePercent / 100.0; // Convert to decimal

            double wickRatio = Math.Max(wickUpPercent, wickDownPercent)
                   / ((highPrice - lowPrice) / avgPrice);


            double bidVolume = market.BidLevels.Take(5).Sum(l => l.Quantity);
            double askVolume = market.AskLevels.Take(5).Sum(l => l.Quantity);
            double totalVol = bidVolume + askVolume;

            TradingSignal signal = null;


            if (wickRatio < _params.MinWickRatio)
                return signal;

            if (volumeSpike < _params.MinVolumeSpike)
                return signal;


            // Downward wick (liquidation of longs)
            if (wickDownPercent >= wickThreshold)
            {
                if (totalVol > 0)
                {
                    double obi = (bidVolume - askVolume) / totalVol;

                    if (_params.RequireOBIConfirmation && !(obi > _config.LiqObiConfirmation))
                        return signal;

                    else
                    {
                        _totalSignals++;

                        signal = new TradingSignal
                        {
                            Symbol = market.Symbol,
                            Strategy = "LiquidationWick",
                            Side = Side.Buy, // Buy the wick
                            Price = market.BestAsk,
                            GeneratedAt = DateTime.UtcNow,
                            Confidence = Math.Min(wickDownPercent / 0.01, 1.0)
                        };

                        signal.TargetPrice = avgPrice; // Target back to pre-wick
                        signal.StopPrice = lowPrice * 0.998; // Stop below wick
                        signal.Quantity = 5000.0 / market.MidPrice;

                        signal.Metadata["wickPercent"] = wickDownPercent;
                        signal.Metadata["volumeSpike"] = volumeSpike;
                        signal.Metadata["obi"] = obi;
                    }
                }
            }
            // Upward wick (liquidation of shorts)
            else if (wickUpPercent >= wickThreshold)
            {
                if (totalVol > 0)
                {
                    double obi = (bidVolume - askVolume) / totalVol;

                    // OBI shows selling pressure after wick
                    if (_params.RequireOBIConfirmation && !(obi < -_config.LiqObiConfirmation))
                        return signal;

                    else
                    {
                        _totalSignals++;

                        signal = new TradingSignal
                        {
                            Symbol = market.Symbol,
                            Strategy = "LiquidationWick",
                            Side = Side.Sell, // Sell the wick
                            Price = market.BestBid,
                            GeneratedAt = DateTime.UtcNow,
                            Confidence = Math.Min(wickUpPercent / 0.01, 1.0)
                        };

                        signal.TargetPrice = avgPrice;
                        signal.StopPrice = highPrice * 1.002; // Stop above wick
                        signal.Quantity = 5000.0 / market.MidPrice;

                        signal.Metadata["wickPercent"] = wickUpPercent;
                        signal.Metadata["volumeSpike"] = volumeSpike;
                        signal.Metadata["obi"] = obi;
                    }
                }
            }

            // Apply regime-based size adjustment
            if (signal != null)
            {
                signal.Confidence *= sizeMultiplier;
                signal.Quantity *= sizeMultiplier;
            }

            return signal;
        }

        public void RefreshParameters()
        {
            _params = _paramManager.GetParameters<LiquidationWickParameters>();
        }


        public void RecordTradeResult(double pnl)
        {
            _totalPnL += pnl;
        }

        public (int signals, double pnl) GetStats()
        {
            return (_totalSignals, _totalPnL);
        }
    }
}
