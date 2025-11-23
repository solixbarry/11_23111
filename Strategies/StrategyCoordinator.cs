using System;
using System.Collections.Generic;
using System.Linq;
using NYCAlphaTrader.Core;
using TradingSystem.Configuration;
using TradingSystem.Core;

namespace NYCAlphaTrader.Strategies
{
    /// <summary>
    /// Strategy Coordinator - orchestrates all trading strategies
    /// Manages signal generation from multiple sources
    /// </summary>
    public class StrategyCoordinator
    {
        private readonly TradingConfig _config;
        private readonly RiskManager _riskManager;

        private readonly OBIStrategy _obiStrategy;
        private readonly MeanReversionStrategy _meanReversionStrategy;
        private readonly LiquidationWickStrategy _liquidationWickStrategy;

        private int signalsNotExec = 0;

        public StrategyCoordinator(TradingConfig config, RiskManager riskManager,
                                    OBIStrategy obiStrategy, MeanReversionStrategy meanReversionStrategy,
                                    LiquidationWickStrategy liquidationWickStrategy)
        {
            _config = config;
            _riskManager = riskManager;

            _obiStrategy = obiStrategy;
            _meanReversionStrategy = meanReversionStrategy;
            _liquidationWickStrategy = liquidationWickStrategy;
        }

        public List<TradingSignal> ProcessMarketUpdate(MarketData market, SignalThrottler throttler, MarketRegime regime)
        {
            var signals = new List<TradingSignal>();

            // Update price history for strategies that need it
            _meanReversionStrategy.UpdatePrice(market.MidPrice, market.Volume24h);
            _liquidationWickStrategy.UpdatePrice(market.MidPrice, market.Volume24h);

            // Check if we're in off-hours (higher alpha period)
            bool isOffHours = IsOffHours();

            // 1. ORDER BOOK IMBALANCE
            if (_config.EnableOBI)
            {
                var obiSignal = _obiStrategy.Analyze(market, regime);
                if (obiSignal != null && obiSignal.IsValid)
                {
                    // Boost confidence during off-hours
                    if (isOffHours)
                    {
                        obiSignal.Confidence *= 1.2;
                        obiSignal.Metadata["offHours"] = true;
                    }

                    // Execute with throttling
                    if (obiSignal.Confidence >= 0.6 &&
                        throttler.ShouldAllowSignal("OBI"))
                    {
                        signals.Add(obiSignal);
                    }
                    else signalsNotExec++;

                }
            }

            // 2. MEAN REVERSION (works best off-hours)
            if (_config.EnableMeanReversion && (isOffHours || true)) // Can run 24/7 but better off-hours
            {
                var mrSignal = _meanReversionStrategy.Analyze(market, regime);
                if (mrSignal != null && mrSignal.IsValid)
                {
                    if (isOffHours)
                    {
                        mrSignal.Confidence *= 1.3; // Much better off-hours
                        mrSignal.Metadata["offHours"] = true;
                    }

                    // Execute with throttling
                    if (mrSignal.Confidence >= 0.6 &&
                    throttler.ShouldAllowSignal("MeanReversion"))
                    {
                        signals.Add(mrSignal);
                    }
                    else signalsNotExec++;
                }
            }

            // 3. LIQUIDATION WICK CAPTURE
            if (_config.EnableLiquidationWicks)
            {
                var liqSignal = _liquidationWickStrategy.Analyze(market, regime);
                if (liqSignal != null && liqSignal.IsValid)
                {
                    signals.Add(liqSignal);
                }
            }

            // Filter signals by risk limits
            var validSignals = new List<TradingSignal>();
            foreach (var signal in signals)
            {
                if (_riskManager.CheckOrder(signal))
                {
                    validSignals.Add(signal);
                }
            }

            foreach (var signal in validSignals)
            {
                Console.WriteLine($"Quantity: {signal.Quantity}");
                double stepSize = 0.001; // get from exchangeInfo ideally
                signal.Quantity = Math.Floor(signal.Quantity / stepSize) * stepSize;
                Console.WriteLine($"Quantity: {signal.Quantity}");
            }
            return validSignals;
        }

        private bool IsOffHours()
        {
            if (!_config.EnableOffHoursTrading)
                return false;

            var currentTime = DateTime.UtcNow.AddHours(-5); // Convert to EST
            var timeOfDay = currentTime.TimeOfDay;

            // Off-hours: 11pm-5am EST
            return timeOfDay >= _config.OffHoursStart || timeOfDay < _config.OffHoursEnd;
        }

        public StrategyStats GetStats()
        {
            var obiStats = _obiStrategy.GetStats();
            var mrStats = _meanReversionStrategy.GetStats();
            var liqStats = _liquidationWickStrategy.GetStats();

            var totalSignals = obiStats.signals + mrStats.signals + liqStats.signals - signalsNotExec;
            Console.WriteLine(
    $"OBI: {obiStats.signals}, " +
    $"MR: {mrStats.signals}, " +
    $"LIQ: {liqStats.signals}, " +
    $"NotExec: {signalsNotExec}, " +
    $"TotalSignals: {totalSignals}"
);
            var totalPnL = obiStats.pnl + mrStats.pnl + liqStats.pnl;

            return new StrategyStats
            {
                TotalSignals = totalSignals,
                TotalPnL = totalPnL,
                WinRate = obiStats.winRate, // OBI dominates
                StrategyPnL = new Dictionary<string, double>
                {
                    ["OBI"] = obiStats.pnl,
                    ["MeanReversion"] = mrStats.pnl,
                    ["LiquidationWick"] = liqStats.pnl
                }
            };
        }

        public void OnFill(Fill fill, bool isWin)
        {
            // Track strategy performance
            // This would need to know which strategy generated the order
            // Simplified for now
        }
    }
}
