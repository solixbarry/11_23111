using System;
using System.Collections.Generic;

namespace TradingSystem.Core
{
    /// <summary>
    /// Prevents signal overtrading by enforcing minimum time between signals
    /// </summary>
    public class SignalThrottler
    {
        private readonly Dictionary<string, DateTime> _lastSignalTime;
        private readonly Dictionary<string, TimeSpan> _minTimeBetweenSignals;
        private readonly TimeSpan _defaultMinTime;
        
        public SignalThrottler(int defaultMinSeconds = 30)
        {
            _lastSignalTime = new Dictionary<string, DateTime>();
            _minTimeBetweenSignals = new Dictionary<string, TimeSpan>();
            _defaultMinTime = TimeSpan.FromSeconds(defaultMinSeconds);
        }
        
        /// <summary>
        /// Set strategy-specific minimum time
        /// </summary>
        public void SetMinTime(string strategyName, int seconds)
        {
            _minTimeBetweenSignals[strategyName] = TimeSpan.FromSeconds(seconds);
        }
        
        /// <summary>
        /// Check if signal should be allowed
        /// </summary>
        public bool ShouldAllowSignal(string strategyName)
        {
            // First signal always allowed
            if (!_lastSignalTime.ContainsKey(strategyName))
            {
                _lastSignalTime[strategyName] = DateTime.UtcNow;
                Console.WriteLine($"[Throttler] {strategyName}: First signal allowed");
                return true;
            }
            
            // Get minimum time for this strategy
            var minTime = _minTimeBetweenSignals.ContainsKey(strategyName) 
                ? _minTimeBetweenSignals[strategyName] 
                : _defaultMinTime;
            
            // Calculate time since last signal
            var timeSinceLastSignal = DateTime.UtcNow - _lastSignalTime[strategyName];
            
            if (timeSinceLastSignal >= minTime)
            {
                _lastSignalTime[strategyName] = DateTime.UtcNow;
                Console.WriteLine($"[Throttler] {strategyName}: Signal allowed (waited {timeSinceLastSignal.TotalSeconds:F1}s)");
                return true;
            }
            
            // Too soon
            var waitTime = minTime - timeSinceLastSignal;
            Console.WriteLine($"[Throttler] {strategyName}: Signal BLOCKED (wait {waitTime.TotalSeconds:F1}s more)");
            return false;
        }
        
        /// <summary>
        /// Reset throttler (useful for testing)
        /// </summary>
        public void Reset()
        {
            _lastSignalTime.Clear();
            Console.WriteLine("[Throttler] Reset complete");
        }
        
        /// <summary>
        /// Get time until next signal allowed
        /// </summary>
        public TimeSpan GetTimeUntilNextSignal(string strategyName)
        {
            if (!_lastSignalTime.ContainsKey(strategyName))
                return TimeSpan.Zero;
            
            var minTime = _minTimeBetweenSignals.ContainsKey(strategyName) 
                ? _minTimeBetweenSignals[strategyName] 
                : _defaultMinTime;
            
            var elapsed = DateTime.UtcNow - _lastSignalTime[strategyName];
            return elapsed < minTime ? minTime - elapsed : TimeSpan.Zero;
        }
    }
}