using System;
using System.Collections.Generic;

namespace TradingSystem.Configuration
{
    /// <summary>
    /// Defines the trading environment for parameter selection
    /// </summary>
    public enum TradingEnvironment
    {
        Testnet,
        Production,
        Backtesting
    }

    /// <summary>
    /// Base class for strategy-specific parameters
    /// </summary>
    public abstract class StrategyParameterSet
    {
        public TradingEnvironment Environment { get; set; }
        public string StrategyName { get; set; }
        
        /// <summary>
        /// Validates that all parameters are within acceptable ranges
        /// </summary>
        public abstract bool Validate(out string errorMessage);
    }

    /// <summary>
    /// Order Book Imbalance strategy parameters
    /// </summary>
    public class OBIParameters : StrategyParameterSet
    {
        // Spread constraints
        public double MaxSpreadBps { get; set; }
        
        // Imbalance thresholds
        public double MinImbalanceThreshold { get; set; }
        public double MaxImbalanceThreshold { get; set; }
        
        // Volume requirements
        public double MinTotalVolume { get; set; }
        public double MinBidVolume { get; set; }
        public double MinAskVolume { get; set; }
        
        // Signal confidence
        public int MinConfirmationTicks { get; set; }
        
        public override bool Validate(out string errorMessage)
        {
            if (MaxSpreadBps <= 0)
            {
                errorMessage = "MaxSpreadBps must be positive";
                return false;
            }
            
            if (MinImbalanceThreshold <= 0.5 || MinImbalanceThreshold >= 1.0)
            {
                errorMessage = "MinImbalanceThreshold must be between 0.5 and 1.0";
                return false;
            }
            
            if (MaxImbalanceThreshold <= MinImbalanceThreshold)
            {
                errorMessage = "MaxImbalanceThreshold must exceed MinImbalanceThreshold";
                return false;
            }
            
            if (MinTotalVolume < 0)
            {
                errorMessage = "MinTotalVolume cannot be negative";
                return false;
            }
            
            errorMessage = null;
            return true;
        }
    }

    /// <summary>
    /// Mean Reversion strategy parameters
    /// </summary>
    public class MeanReversionParameters : StrategyParameterSet
    {
        // VWAP deviation thresholds
        public double MinDeviationPercent { get; set; }
        public double MaxDeviationPercent { get; set; }
        
        // Z-score requirements
        public double MinZScore { get; set; }
        public double MaxZScore { get; set; }
        
        // Volume confirmation
        public double MinVolumeRatio { get; set; }
        public double MinAbsoluteVolume { get; set; }
        
        // Statistical parameters
        public int LookbackPeriod { get; set; }
        public int MinDataPoints { get; set; }
        
        // Risk controls
        public double MaxPositionSizePercent { get; set; }
        
        public override bool Validate(out string errorMessage)
        {
            if (MinZScore < 0)
            {
                errorMessage = "MinZScore cannot be negative";
                return false;
            }
            
            if (MaxZScore <= MinZScore)
            {
                errorMessage = "MaxZScore must exceed MinZScore";
                return false;
            }
            
            if (MinVolumeRatio < 0)
            {
                errorMessage = "MinVolumeRatio cannot be negative";
                return false;
            }
            
            if (LookbackPeriod < 10)
            {
                errorMessage = "LookbackPeriod must be at least 10 for statistical validity";
                return false;
            }
            
            if (MinDataPoints > LookbackPeriod)
            {
                errorMessage = "MinDataPoints cannot exceed LookbackPeriod";
                return false;
            }
            
            errorMessage = null;
            return true;
        }
    }

    /// <summary>
    /// Liquidation Wick strategy parameters
    /// </summary>
    public class LiquidationWickParameters : StrategyParameterSet
    {
        // Wick detection
        public double MinWickRatio { get; set; }
        public double MaxWickRatio { get; set; }
        public int WickLookbackTicks { get; set; }
        
        // Volume spike requirements
        public double MinVolumeSpike { get; set; }
        public int VolumeLookbackPeriod { get; set; }
        
        // OBI confirmation
        public bool RequireOBIConfirmation { get; set; }
        public double MinOBIImbalance { get; set; }
        
        // Recovery timing
        public int MaxRecoveryTicks { get; set; }
        public double MinRecoveryPercent { get; set; }
        
        public override bool Validate(out string errorMessage)
        {
            if (MinWickRatio <= 0 || MinWickRatio >= 1.0)
            {
                errorMessage = "MinWickRatio must be between 0 and 1.0";
                return false;
            }
            
            if (MaxWickRatio <= MinWickRatio)
            {
                errorMessage = "MaxWickRatio must exceed MinWickRatio";
                return false;
            }
            
            if (MinVolumeSpike < 1.0)
            {
                errorMessage = "MinVolumeSpike must be at least 1.0";
                return false;
            }
            
            if (WickLookbackTicks < 2)
            {
                errorMessage = "WickLookbackTicks must be at least 2";
                return false;
            }
            
            errorMessage = null;
            return true;
        }
    }

    /// <summary>
    /// Breakout strategy parameters (for future use)
    /// </summary>
    public class BreakoutParameters : StrategyParameterSet
    {
        // Breakout detection
        public double MinBreakoutPercent { get; set; }
        public int ConsolidationPeriod { get; set; }
        public double MaxConsolidationRange { get; set; }
        
        // Volume confirmation
        public double MinVolumeIncrease { get; set; }
        
        // MALP integration
        public bool UseMalpFilter { get; set; }
        public double MinMalpProbability { get; set; }
        
        public override bool Validate(out string errorMessage)
        {
            if (MinBreakoutPercent <= 0)
            {
                errorMessage = "MinBreakoutPercent must be positive";
                return false;
            }
            
            if (ConsolidationPeriod < 5)
            {
                errorMessage = "ConsolidationPeriod must be at least 5";
                return false;
            }
            
            if (UseMalpFilter && (MinMalpProbability < 0 || MinMalpProbability > 1.0))
            {
                errorMessage = "MinMalpProbability must be between 0 and 1.0";
                return false;
            }
            
            errorMessage = null;
            return true;
        }
    }

    /// <summary>
    /// Central configuration manager for all strategy parameters
    /// </summary>
    public class StrategyParameterManager
    {
        private readonly Dictionary<string, StrategyParameterSet> _parameterSets;
        private TradingEnvironment _currentEnvironment;
        
        public StrategyParameterManager(TradingEnvironment environment = TradingEnvironment.Testnet)
        {
            _parameterSets = new Dictionary<string, StrategyParameterSet>();
            _currentEnvironment = environment;
            InitializeDefaultParameters();
        }
        
        /// <summary>
        /// Gets the current trading environment
        /// </summary>
        public TradingEnvironment CurrentEnvironment => _currentEnvironment;
        
        /// <summary>
        /// Switches to a different environment and reloads parameters
        /// </summary>
        public void SwitchEnvironment(TradingEnvironment newEnvironment)
        {
            if (_currentEnvironment == newEnvironment)
                return;
                
            Console.WriteLine($"[ParameterManager] Switching from {_currentEnvironment} to {newEnvironment}");
            _currentEnvironment = newEnvironment;
            InitializeDefaultParameters();
            
            // Validate all parameter sets after switching
            ValidateAllParameters();
        }
        
        /// <summary>
        /// Retrieves parameters for a specific strategy
        /// </summary>
        public T GetParameters<T>() where T : StrategyParameterSet
        {
            string typeName = typeof(T).Name;
            
            if (_parameterSets.TryGetValue(typeName, out var paramSet))
            {
                return (T)paramSet;
            }
            
            throw new InvalidOperationException($"Parameter set not found: {typeName}");
        }
        
        /// <summary>
        /// Updates parameters for a specific strategy (useful for live tuning)
        /// </summary>
        public void UpdateParameters<T>(T parameters) where T : StrategyParameterSet
        {
            if (!parameters.Validate(out string error))
            {
                throw new ArgumentException($"Invalid parameters: {error}");
            }
            
            string typeName = typeof(T).Name;
            parameters.Environment = _currentEnvironment;
            _parameterSets[typeName] = parameters;
            
            Console.WriteLine($"[ParameterManager] Updated {typeName} for {_currentEnvironment}");
        }
        
        /// <summary>
        /// Validates all loaded parameter sets
        /// </summary>
        private void ValidateAllParameters()
        {
            foreach (var kvp in _parameterSets)
            {
                if (!kvp.Value.Validate(out string error))
                {
                    throw new InvalidOperationException(
                        $"Invalid parameter set {kvp.Key} in {_currentEnvironment}: {error}");
                }
            }
            
            Console.WriteLine($"[ParameterManager] All parameters validated for {_currentEnvironment}");
        }
        
        /// <summary>
        /// Initializes default parameters based on current environment
        /// </summary>
        private void InitializeDefaultParameters()
        {
            _parameterSets.Clear();
            
            // Initialize OBI parameters
            _parameterSets["OBIParameters"] = CreateOBIParameters();
            
            // Initialize Mean Reversion parameters
            _parameterSets["MeanReversionParameters"] = CreateMeanReversionParameters();
            
            // Initialize Liquidation Wick parameters
            _parameterSets["LiquidationWickParameters"] = CreateLiquidationWickParameters();
            
            // Initialize Breakout parameters
            _parameterSets["BreakoutParameters"] = CreateBreakoutParameters();
        }
        
        private OBIParameters CreateOBIParameters()
        {
            switch (_currentEnvironment)
            {
                case TradingEnvironment.Testnet:
                    return new OBIParameters
                    {
                        Environment = TradingEnvironment.Testnet,
                        StrategyName = "OrderBookImbalance",
                        
                        // Relaxed spread for low-liquidity testnet
                        MaxSpreadBps = 25.0,
                        
                        // Lower imbalance thresholds
                        MinImbalanceThreshold = 0.62,
                        MaxImbalanceThreshold = 0.95,
                        
                        // Reduced volume requirements
                        MinTotalVolume = 1.0,
                        MinBidVolume = 0.1,
                        MinAskVolume = 0.1,
                        
                        // Quick confirmation
                        MinConfirmationTicks = 1
                    };
                    
                case TradingEnvironment.Production:
                    return new OBIParameters
                    {
                        Environment = TradingEnvironment.Production,
                        StrategyName = "OrderBookImbalance",
                        
                        // Strict spread control for real markets
                        MaxSpreadBps = 1.5,
                        
                        // Aggressive imbalance detection
                        MinImbalanceThreshold = 0.65,
                        MaxImbalanceThreshold = 0.98,
                        
                        // Production volume requirements
                        MinTotalVolume = 1.0,
                        MinBidVolume = 0.3,
                        MinAskVolume = 0.3,
                        
                        // Require confirmation
                        MinConfirmationTicks = 2
                    };
                    
                case TradingEnvironment.Backtesting:
                    return new OBIParameters
                    {
                        Environment = TradingEnvironment.Backtesting,
                        StrategyName = "OrderBookImbalance",
                        
                        // Moderate thresholds for historical analysis
                        MaxSpreadBps = 3.0,
                        MinImbalanceThreshold = 0.60,
                        MaxImbalanceThreshold = 0.97,
                        MinTotalVolume = 0.8,
                        MinBidVolume = 0.2,
                        MinAskVolume = 0.2,
                        MinConfirmationTicks = 1
                    };
                    
                default:
                    throw new ArgumentException($"Unknown environment: {_currentEnvironment}");
            }
        }
        
        private MeanReversionParameters CreateMeanReversionParameters()
        {
            switch (_currentEnvironment)
            {
                case TradingEnvironment.Testnet:
                    return new MeanReversionParameters
                    {
                        Environment = TradingEnvironment.Testnet,
                        StrategyName = "MeanReversion",
                        
                        // Relaxed deviation thresholds
                        MinDeviationPercent = 0.3,
                        MaxDeviationPercent = 3.0,
                        
                        // Lower z-score requirements
                        MinZScore = 1.5,
                        MaxZScore = 4.0,
                        
                        // Reduced volume confirmation
                        MinVolumeRatio = 0.8,
                        MinAbsoluteVolume = 5.0,
                        
                        // Statistical parameters
                        LookbackPeriod = 20,
                        MinDataPoints = 15,
                        
                        // Risk control
                        MaxPositionSizePercent = 5.0
                    };
                    
                case TradingEnvironment.Production:
                    return new MeanReversionParameters
                    {
                        Environment = TradingEnvironment.Production,
                        StrategyName = "MeanReversion",
                        
                        // Conservative deviation bounds
                        MinDeviationPercent = 0.5,
                        MaxDeviationPercent = 2.5,
                        
                        // Strict z-score requirements
                        MinZScore = 2.0,
                        MaxZScore = 5.0,
                        
                        // Strong volume confirmation
                        MinVolumeRatio = 1.5,
                        MinAbsoluteVolume = 10.0,
                        
                        // Robust statistics
                        LookbackPeriod = 30,
                        MinDataPoints = 25,
                        
                        // Conservative risk
                        MaxPositionSizePercent = 3.0
                    };
                    
                case TradingEnvironment.Backtesting:
                    return new MeanReversionParameters
                    {
                        Environment = TradingEnvironment.Backtesting,
                        StrategyName = "MeanReversion",
                        
                        MinDeviationPercent = 0.4,
                        MaxDeviationPercent = 2.8,
                        MinZScore = 1.8,
                        MaxZScore = 4.5,
                        MinVolumeRatio = 1.2,
                        MinAbsoluteVolume = 8.0,
                        LookbackPeriod = 25,
                        MinDataPoints = 20,
                        MaxPositionSizePercent = 4.0
                    };
                    
                default:
                    throw new ArgumentException($"Unknown environment: {_currentEnvironment}");
            }
        }
        
        private LiquidationWickParameters CreateLiquidationWickParameters()
        {
            switch (_currentEnvironment)
            {
                case TradingEnvironment.Testnet:
                    return new LiquidationWickParameters
                    {
                        Environment = TradingEnvironment.Testnet,
                        StrategyName = "LiquidationWick",
                        
                        // Relaxed wick detection
                        MinWickRatio = 0.4,
                        MaxWickRatio = 0.9,
                        WickLookbackTicks = 3,
                        
                        // Lower volume spike threshold
                        MinVolumeSpike = 1.2,
                        VolumeLookbackPeriod = 5,
                        
                        // Optional OBI confirmation
                        RequireOBIConfirmation = false,
                        MinOBIImbalance = 0.55,
                        
                        // Quick recovery window
                        MaxRecoveryTicks = 5,
                        MinRecoveryPercent = 30.0
                    };
                    
                case TradingEnvironment.Production:
                    return new LiquidationWickParameters
                    {
                        Environment = TradingEnvironment.Production,
                        StrategyName = "LiquidationWick",
                        
                        // Strict wick requirements
                        MinWickRatio = 0.5,
                        MaxWickRatio = 0.85,
                        WickLookbackTicks = 5,
                        
                        // Strong volume spike required
                        MinVolumeSpike = 2.0,
                        VolumeLookbackPeriod = 10,
                        
                        // Mandatory OBI confirmation
                        RequireOBIConfirmation = true,
                        MinOBIImbalance = 0.65,
                        
                        // Conservative recovery
                        MaxRecoveryTicks = 10,
                        MinRecoveryPercent = 50.0
                    };
                    
                case TradingEnvironment.Backtesting:
                    return new LiquidationWickParameters
                    {
                        Environment = TradingEnvironment.Backtesting,
                        StrategyName = "LiquidationWick",
                        
                        MinWickRatio = 0.45,
                        MaxWickRatio = 0.88,
                        WickLookbackTicks = 4,
                        MinVolumeSpike = 1.6,
                        VolumeLookbackPeriod = 8,
                        RequireOBIConfirmation = true,
                        MinOBIImbalance = 0.60,
                        MaxRecoveryTicks = 8,
                        MinRecoveryPercent = 40.0
                    };
                    
                default:
                    throw new ArgumentException($"Unknown environment: {_currentEnvironment}");
            }
        }
        
        private BreakoutParameters CreateBreakoutParameters()
        {
            switch (_currentEnvironment)
            {
                case TradingEnvironment.Testnet:
                    return new BreakoutParameters
                    {
                        Environment = TradingEnvironment.Testnet,
                        StrategyName = "Breakout",
                        
                        MinBreakoutPercent = 0.4,
                        ConsolidationPeriod = 10,
                        MaxConsolidationRange = 0.8,
                        MinVolumeIncrease = 1.3,
                        UseMalpFilter = false,
                        MinMalpProbability = 0.5
                    };
                    
                case TradingEnvironment.Production:
                    return new BreakoutParameters
                    {
                        Environment = TradingEnvironment.Production,
                        StrategyName = "Breakout",
                        
                        MinBreakoutPercent = 0.6,
                        ConsolidationPeriod = 15,
                        MaxConsolidationRange = 0.5,
                        MinVolumeIncrease = 1.8,
                        UseMalpFilter = true,
                        MinMalpProbability = 0.65
                    };
                    
                case TradingEnvironment.Backtesting:
                    return new BreakoutParameters
                    {
                        Environment = TradingEnvironment.Backtesting,
                        StrategyName = "Breakout",
                        
                        MinBreakoutPercent = 0.5,
                        ConsolidationPeriod = 12,
                        MaxConsolidationRange = 0.65,
                        MinVolumeIncrease = 1.5,
                        UseMalpFilter = true,
                        MinMalpProbability = 0.60
                    };
                    
                default:
                    throw new ArgumentException($"Unknown environment: {_currentEnvironment}");
            }
        }
        
        /// <summary>
        /// Exports current parameters to a configuration file (for persistence)
        /// </summary>
        public string ExportConfiguration()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Trading System Configuration - {_currentEnvironment}");
            sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();
            
            foreach (var kvp in _parameterSets)
            {
                sb.AppendLine($"[{kvp.Key}]");
                var props = kvp.Value.GetType().GetProperties();
                foreach (var prop in props)
                {
                    if (prop.Name == "Environment" || prop.Name == "StrategyName")
                        continue;
                    sb.AppendLine($"{prop.Name} = {prop.GetValue(kvp.Value)}");
                }
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Logs current parameter configuration to console
        /// </summary>
        public void LogConfiguration()
        {
            Console.WriteLine($"\n{'=',60}");
            Console.WriteLine($"STRATEGY PARAMETER CONFIGURATION - {_currentEnvironment}");
            Console.WriteLine($"{'=',60}\n");
            
            foreach (var kvp in _parameterSets)
            {
                Console.WriteLine($"Strategy: {kvp.Value.StrategyName}");
                Console.WriteLine($"Type: {kvp.Key}");
                
                var props = kvp.Value.GetType().GetProperties();
                foreach (var prop in props)
                {
                    if (prop.Name == "Environment" || prop.Name == "StrategyName")
                        continue;
                    Console.WriteLine($"  {prop.Name}: {prop.GetValue(kvp.Value)}");
                }
                Console.WriteLine();
            }
        }
    }
    
    /// <summary>
    /// Example usage and integration pattern
    /// </summary>
    public class ParameterManagerExample
    {
        public static void DemonstrateUsage()
        {
            // Initialize with testnet parameters
            var paramManager = new StrategyParameterManager(TradingEnvironment.Testnet);
            
            // Get OBI parameters
            var obiParams = paramManager.GetParameters<OBIParameters>();
            Console.WriteLine($"OBI MaxSpreadBps (Testnet): {obiParams.MaxSpreadBps}");
            
            // Get Mean Reversion parameters
            var mrParams = paramManager.GetParameters<MeanReversionParameters>();
            Console.WriteLine($"MR MinVolumeRatio (Testnet): {mrParams.MinVolumeRatio}");
            
            // Switch to production
            paramManager.SwitchEnvironment(TradingEnvironment.Production);
            
            // Parameters automatically updated
            obiParams = paramManager.GetParameters<OBIParameters>();
            Console.WriteLine($"OBI MaxSpreadBps (Production): {obiParams.MaxSpreadBps}");
            
            mrParams = paramManager.GetParameters<MeanReversionParameters>();
            Console.WriteLine($"MR MinVolumeRatio (Production): {mrParams.MinVolumeRatio}");
            
            // Export configuration
            string config = paramManager.ExportConfiguration();
            System.IO.File.WriteAllText("strategy_config.txt", config);
            
            // Log full configuration
            paramManager.LogConfiguration();
        }
    }
}
