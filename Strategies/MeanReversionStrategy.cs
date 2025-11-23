using System;
using System.Collections.Generic;
using System.Linq;
using NYCAlphaTrader.Core;
using TradingSystem.Configuration;

namespace NYCAlphaTrader.Strategies
{
    /// <summary>
    /// Mean Reversion Strategy
    /// Trades price deviations from VWAP during off-hours
    /// Expected: $30-70/day
    /// </summary>
    public class MeanReversionStrategy
    {
        private readonly TradingConfig _config;
        private readonly List<double> _priceHistory = new List<double>();
        private readonly List<double> _volumeHistory = new List<double>();
        private double _vwap = 0;
        private double _volumeMA = 0;
        private int _totalSignals = 0;
        private double _totalPnL = 0;

        private const int HistoryWindow = 200;
        private readonly StrategyParameterManager _paramManager;
        private MeanReversionParameters _params;

        public MeanReversionStrategy(TradingConfig config, StrategyParameterManager paramManager)
        {
            _config = config;
            _paramManager = paramManager;
            _params = paramManager.GetParameters<MeanReversionParameters>();
        }

        public void UpdatePrice(double price, double volume)
        {
            _priceHistory.Add(price);
            _volumeHistory.Add(volume);

            if (_priceHistory.Count > HistoryWindow)
            {
                _priceHistory.RemoveAt(0);
                _volumeHistory.RemoveAt(0);
            }

            RecalculateMetrics();
        }

        private void RecalculateMetrics()
        {
            if (_priceHistory.Count < 20)
                return;

            // Calculate VWAP
            double totalPV = 0, totalV = 0;
            for (int i = 0; i < _priceHistory.Count; i++)
            {
                totalPV += _priceHistory[i] * _volumeHistory[i];
                totalV += _volumeHistory[i];
            }
            _vwap = totalV > 0 ? totalPV / totalV : _priceHistory.Average();

            // Calculate volume MA
            _volumeMA = _volumeHistory.Average();
        }

        public TradingSignal Analyze(MarketData market, MarketRegime regime)
        {
            if (!_config.EnableMeanReversion)
                return null;

            // ===== CRITICAL: REGIME FILTER - PREVENTS -$386 LOSSES =====
            // BLOCK mean reversion during downtrends
            if (regime == MarketRegime.Downtrend)
            {
                Console.WriteLine("[MR] ✗ Downtrend detected - mean reversion DISABLED");
                return null;
            }

            if (regime == MarketRegime.HighVolatility)
            {
                Console.WriteLine("[MR] ✗ High volatility - mean reversion DISABLED");
                return null;
            }

            // Only trade in RANGING markets
            if (regime != MarketRegime.Ranging)
            {
                Console.WriteLine($"[MR] ✗ Wrong regime: {regime} - need Ranging");
                return null;
            }
            // ===== END REGIME FILTER =====

            if (_priceHistory.Count < 20)
                return null;

            // Calculate price deviation from VWAP
            double currentPrice = market.MidPrice;
            double priceDeviation = currentPrice - _vwap;

            // Calculate standard deviation
            double variance = _priceHistory.Select(p => Math.Pow(p - _vwap, 2)).Average();
            double stdDev = Math.Sqrt(variance);

            if (stdDev < 0.0001)
                return null;

            double zScore = priceDeviation / stdDev;

            // Volume spike confirmation
            double volumeRatio = market.Volume24h / _volumeMA;

            if (Math.Abs(zScore) < _params.MinZScore)
                return null;

            if (volumeRatio < _params.MinVolumeRatio)
                return null;

            _totalSignals++;

            var signal = new TradingSignal
            {
                Symbol = market.Symbol,
                Strategy = "MeanReversion",
                GeneratedAt = DateTime.UtcNow,
                Confidence = Math.Min(Math.Abs(zScore) / 3.0, 1.0)
            };

            // Price too high → short (expect reversion down)
            if (zScore > _config.MrVwapDeviation)
            {
                signal.Side = Side.Sell;
                signal.Price = market.BestBid;
                signal.TargetPrice = _vwap; // Revert to VWAP
                signal.StopPrice = currentPrice * (1.0 + _config.MrStopBps / 10000.0);
            }
            // Price too low → long (expect reversion up)
            else if (zScore < -_config.MrVwapDeviation)
            {
                signal.Side = Side.Buy;
                signal.Price = market.BestAsk;
                signal.TargetPrice = _vwap;
                signal.StopPrice = currentPrice * (1.0 - _config.MrStopBps / 10000.0);
            }

            signal.Quantity = 4000.0 / currentPrice; // $4k notional
            signal.Metadata["zScore"] = zScore;
            signal.Metadata["vwap"] = _vwap;
            signal.Metadata["volumeRatio"] = volumeRatio;

            return signal;
        }

        public void RefreshParameters()
        {
            _params = _paramManager.GetParameters<MeanReversionParameters>();
        }
        public bool ShouldExit(TradingSignal entrySignal, double currentPrice)
        {
            // Exit if price returned close to VWAP
            double distanceFromVwap = Math.Abs(currentPrice - _vwap) / _vwap;
            return distanceFromVwap < 0.0003; // Within 3 bps of VWAP
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
