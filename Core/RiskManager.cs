using System;
using System.Collections.Generic;
using System.Linq;

namespace NYCAlphaTrader.Core
{
    public class RiskManager
    {
        private readonly TradingConfig _config;
        private readonly Dictionary<string, Position> _positions = new Dictionary<string, Position>();
        private double _dailyRealizedPnL = 0;
        private double _peakDailyPnL = 0;
        private readonly object _lock = new object();

        public RiskManager(TradingConfig config)
        {
            _config = config;
        }

        public bool CheckOrder(TradingSignal signal)
        {
            lock (_lock)
            {
                // Check 1: Daily loss limit
                var totalPnL = GetTotalPnL(signal.Price);
                if (totalPnL < -_config.MaxDailyLoss)
                {
                    Console.WriteLine($"[RISK] Daily loss limit hit: ${totalPnL:F2}");
                    return false;
                }

                // Check 2: Trailing stop from peak
                var drawdownFromPeak = _peakDailyPnL - totalPnL;
                var maxDrawdown = _config.MaxDailyLoss * _config.TrailingStopPercent;
                if (drawdownFromPeak > maxDrawdown)
                {
                    Console.WriteLine($"[RISK] Trailing stop hit: drawdown ${drawdownFromPeak:F2}");
                    return false;
                }

                // Check 3: Position size limit
                var notional = signal.Quantity * signal.Price;
                if (notional > _config.MaxPositionSize)
                {
                    Console.WriteLine($"[RISK] Position too large: ${notional:F2}");
                    return false;
                }

                // Check 4: Spread filter
                // This would need current market data - skipping for now

                return true;
            }
        }

        public void OnFill(Fill fill)
        {
            lock (_lock)
            {
                if (!_positions.ContainsKey(fill.Symbol))
                {
                    _positions[fill.Symbol] = new Position
                    {
                        Symbol = fill.Symbol,
                        Quantity = 0,
                        AvgPrice = 0,
                        OpenedAt = DateTime.UtcNow
                    };
                }

                var pos = _positions[fill.Symbol];
                var signedQty = fill.Side == Side.Buy ? fill.Quantity : -fill.Quantity;

                if (pos.IsFlat)
                {
                    // Opening new position
                    pos.Quantity = signedQty;
                    pos.AvgPrice = fill.Price;
                    pos.OpenedAt = fill.Timestamp;
                }
                else if ((pos.IsLong && fill.Side == Side.Buy) || (pos.IsShort && fill.Side == Side.Sell))
                {
                    // Adding to position
                    var totalCost = (pos.Quantity * pos.AvgPrice) + (signedQty * fill.Price);
                    pos.Quantity += signedQty;
                    pos.AvgPrice = totalCost / pos.Quantity;
                }
                else
                {
                    // Closing or reducing position
                    var closedQty = Math.Min(Math.Abs(signedQty), Math.Abs(pos.Quantity));
                    var pnl = closedQty * (fill.Price - pos.AvgPrice) * (pos.IsLong ? 1 : -1);
                    pnl -= fill.Fee;

                    pos.RealizedPnL += pnl;
                    _dailyRealizedPnL += pnl;
                    pos.Quantity += signedQty;

                    // Track peak for trailing stop
                    var total = GetTotalPnL(fill.Price);
                    if (total > _peakDailyPnL)
                    {
                        _peakDailyPnL = total;
                    }
                }
            }
        }

        public void UpdateMarketPrices(Dictionary<string, double> prices)
        {
            lock (_lock)
            {
                foreach (var pos in _positions.Values)
                {
                    if (prices.TryGetValue(pos.Symbol, out var price))
                    {
                        pos.UpdateUnrealized(price);
                    }
                }

                // Update peak
                var totalPnL = _dailyRealizedPnL + _positions.Values.Sum(p => p.UnrealizedPnL);
                if (totalPnL > _peakDailyPnL)
                {
                    _peakDailyPnL = totalPnL;
                }
            }
        }

        public double GetTotalPnL(double genericPrice)
        {
            lock (_lock)
            {
                var unrealized = _positions.Values.Sum(p => p.UnrealizedPnL);
                return _dailyRealizedPnL + unrealized;
            }
        }

        public RiskStats GetRiskStats()
        {
            lock (_lock)
            {
                var unrealized = _positions.Values.Sum(p => p.UnrealizedPnL);
                var totalPnL = _dailyRealizedPnL + unrealized;

                return new RiskStats
                {
                    DailyPnL = totalPnL,
                    PeakPnL = _peakDailyPnL,
                    MaxDrawdown = _peakDailyPnL - totalPnL,
                    GrossExposure = _positions.Values.Sum(p => Math.Abs(p.Quantity * p.AvgPrice)),
                    NetExposure = _positions.Values.Sum(p => p.Quantity * p.AvgPrice),
                    ActivePositions = _positions.Values.Count(p => !p.IsFlat)
                };
            }
        }

        public void ResetDaily()
        {
            lock (_lock)
            {
                _dailyRealizedPnL = 0;
                _peakDailyPnL = 0;
                
                foreach (var pos in _positions.Values)
                {
                    pos.RealizedPnL = 0;
                    pos.UnrealizedPnL = 0;
                }
            }
        }

        public Position GetPosition(string symbol)
        {
            lock (_lock)
            {
                return _positions.TryGetValue(symbol, out var pos) ? pos : null;
            }
        }
    }
}
