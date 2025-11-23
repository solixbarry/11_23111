using System;
using System.Collections.Generic;
using System.Linq;
using NYCAlphaTrader.Core;

namespace NYCAlphaTrader.Core
{
    /// <summary>
    /// Internal Risk Manager - Stealth-mode SL/TP management
    /// Handles stop loss, take profit, and time stops internally without exchange orders
    /// Benefits: Stop hunt protection, dynamic adjustments, faster reaction time
    /// </summary>
    public class InternalRiskManager
    {
        private Dictionary<string, OpenPosition> _positions = new Dictionary<string, OpenPosition>();

        public class OpenPosition
        {
            public string OrderId { get; set; }
            public string Symbol { get; set; }
            public Side Side { get; set; }
            public double EntryPrice { get; set; }
            public double Quantity { get; set; }
            public DateTime EntryTime { get; set; }
            public double StopLossPrice { get; set; }
            public double TakeProfitPrice { get; set; }
            public int MaxHoldSeconds { get; set; }
            public string Strategy { get; set; }
        }

        /// <summary>
        /// Track a new position with stop loss and take profit
        /// </summary>
        public void TrackPosition(string orderId, string symbol, Side side,
                                 double entryPrice, double quantity,
                                 string strategy = "Unknown",
                                 double stopLossPct = 0.003,
                                 double takeProfitPct = 0.005,
                                 int maxHoldSeconds = 300)
        {
            var position = new OpenPosition
            {
                OrderId = orderId,
                Symbol = symbol,
                Side = side,
                EntryPrice = entryPrice,
                Quantity = quantity,
                EntryTime = DateTime.UtcNow,
                MaxHoldSeconds = maxHoldSeconds,  // Default 5 minutes
                Strategy = strategy,
                StopLossPrice = side == Side.Buy ?
                               entryPrice * (1 - stopLossPct) :
                               entryPrice * (1 + stopLossPct),
                TakeProfitPrice = side == Side.Buy ?
                                 entryPrice * (1 + takeProfitPct) :
                                 entryPrice * (1 - takeProfitPct)
            };

            _positions[orderId] = position;
            Console.WriteLine($"[RISK] Tracking {strategy} position {orderId}: Entry=${entryPrice:F2}, SL=${position.StopLossPrice:F2}, TP=${position.TakeProfitPrice:F2}");
        }

        /// <summary>
        /// Check all open positions for exit conditions (SL/TP/Time)
        /// Returns list of positions that need to be closed
        /// </summary>
        public List<OpenPosition> CheckExits(double currentPrice)
        {
            var positionsToExit = new List<OpenPosition>();
            var now = DateTime.UtcNow;

            foreach (var position in _positions.Values.ToList())
            {
                bool shouldExit = false;
                string exitReason = "";

                // Check stop loss (LONG)
                if (position.Side == Side.Buy && currentPrice <= position.StopLossPrice)
                {
                    shouldExit = true;
                    exitReason = "StopLoss";
                }
                // Check stop loss (SHORT)
                else if (position.Side == Side.Sell && currentPrice >= position.StopLossPrice)
                {
                    shouldExit = true;
                    exitReason = "StopLoss";
                }

                // Check take profit (LONG)
                if (position.Side == Side.Buy && currentPrice >= position.TakeProfitPrice)
                {
                    shouldExit = true;
                    exitReason = "TakeProfit";
                }
                // Check take profit (SHORT)
                else if (position.Side == Side.Sell && currentPrice <= position.TakeProfitPrice)
                {
                    shouldExit = true;
                    exitReason = "TakeProfit";
                }

                // Check time stop
                var holdTime = (now - position.EntryTime).TotalSeconds;
                if (holdTime > position.MaxHoldSeconds)
                {
                    shouldExit = true;
                    exitReason = "TimeStop";
                }

                if (shouldExit)
                {
                    double pnl = CalculatePnL(position, currentPrice);
                    Console.WriteLine($"[RISK] Exit signal: {position.Strategy} Order {position.OrderId} - {exitReason} | P&L: ${pnl:F2}");
                    positionsToExit.Add(position);
                    _positions.Remove(position.OrderId);
                }
            }

            return positionsToExit;
        }

        /// <summary>
        /// Calculate P&L for a position
        /// </summary>
        private double CalculatePnL(OpenPosition position, double currentPrice)
        {
            if (position.Side == Side.Buy)
            {
                // Long: P&L = (Current - Entry) * Quantity
                return (currentPrice - position.EntryPrice) * position.Quantity;
            }
            else
            {
                // Short: P&L = (Entry - Current) * Quantity
                return (position.EntryPrice - currentPrice) * position.Quantity;
            }
        }

        /// <summary>
        /// Get count of open positions
        /// </summary>
        public int GetOpenPositionCount()
        {
            return _positions.Count;
        }

        /// <summary>
        /// Get all open positions
        /// </summary>
        public List<OpenPosition> GetOpenPositions()
        {
            return _positions.Values.ToList();
        }

        /// <summary>
        /// Manually close a position (for emergency exits)
        /// </summary>
        public void ClosePosition(string orderId)
        {
            if (_positions.ContainsKey(orderId))
            {
                _positions.Remove(orderId);
                Console.WriteLine($"[RISK] Position {orderId} manually closed");
            }
        }

        /// <summary>
        /// Get status string for logging
        /// </summary>
        public string GetStatus()
        {
            return $"Open Positions: {_positions.Count}";
        }
    }
}
