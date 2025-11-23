using System;
using System.Collections.Generic;
using System.Linq;

namespace NYCAlphaTrader.Core
{
    public class Order
    {
        public string OrderId { get; set; }
        public string ClientOrderId { get; set; }
        public string Symbol { get; set; }
        public Side Side { get; set; }
        public OrderType Type { get; set; }
        public double Price { get; set; }
        public double Quantity { get; set; }
        public double FilledQuantity { get; set; }
        public string Status { get; set; } = "PENDING";
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string Strategy { get; set; }
        
        public bool IsActive => Status == "NEW" || Status == "PARTIALLY_FILLED";
        public bool IsComplete => Status == "FILLED" || Status == "CANCELED" || Status == "REJECTED";
    }

    public class OrderTracker
    {
        private readonly Dictionary<string, Order> _orders = new Dictionary<string, Order>();
        private readonly Dictionary<string, string> _orderIdToClientId = new Dictionary<string, string>();
        private readonly Dictionary<string, List<string>> _symbolOrders = new Dictionary<string, List<string>>();
        private readonly HashSet<string> _activeOrders = new HashSet<string>();
        private readonly object _lock = new object();
        
        private const int MaxOrders = 100000;

        public void TrackOrder(Order order)
        {
            lock (_lock)
            {
                // Auto-cleanup if too many orders
                if (_orders.Count >= MaxOrders)
                {
                    CleanupOldest(1000);
                }

                _orders[order.ClientOrderId] = order;
                
                if (!string.IsNullOrEmpty(order.OrderId))
                {
                    _orderIdToClientId[order.OrderId] = order.ClientOrderId;
                }

                if (!_symbolOrders.ContainsKey(order.Symbol))
                {
                    _symbolOrders[order.Symbol] = new List<string>();
                }
                _symbolOrders[order.Symbol].Add(order.ClientOrderId);

                if (order.IsActive)
                {
                    _activeOrders.Add(order.ClientOrderId);
                }
            }
        }

        public void UpdateOrder(string clientOrderId, Order updated)
        {
            lock (_lock)
            {
                if (!_orders.TryGetValue(clientOrderId, out var existing))
                {
                    return;
                }

                // Update active set
                if (existing.IsActive && !updated.IsActive)
                {
                    _activeOrders.Remove(clientOrderId);
                }
                else if (!existing.IsActive && updated.IsActive)
                {
                    _activeOrders.Add(clientOrderId);
                }

                _orders[clientOrderId] = updated;
            }
        }

        public string GetSymbol(string orderId)
        {
            lock (_lock)
            {
                // Try as exchange order ID
                if (_orderIdToClientId.TryGetValue(orderId, out var clientId))
                {
                    if (_orders.TryGetValue(clientId, out var order))
                    {
                        return order.Symbol;
                    }
                }

                // Try as client order ID
                if (_orders.TryGetValue(orderId, out var directOrder))
                {
                    return directOrder.Symbol;
                }

                return null;
            }
        }

        public Order GetOrder(string clientOrderId)
        {
            lock (_lock)
            {
                return _orders.TryGetValue(clientOrderId, out var order) ? order : null;
            }
        }

        public List<Order> GetActiveOrders()
        {
            lock (_lock)
            {
                return _activeOrders
                    .Select(id => _orders.TryGetValue(id, out var order) ? order : null)
                    .Where(o => o != null)
                    .ToList();
            }
        }

        public List<Order> GetOrdersForSymbol(string symbol)
        {
            lock (_lock)
            {
                if (!_symbolOrders.TryGetValue(symbol, out var ids))
                {
                    return new List<Order>();
                }

                return ids
                    .Select(id => _orders.TryGetValue(id, out var order) ? order : null)
                    .Where(o => o != null)
                    .ToList();
            }
        }

        private void CleanupOldest(int count)
        {
            var completed = _orders.Values
                .Where(o => o.IsComplete && o.CompletedAt.HasValue)
                .OrderBy(o => o.CompletedAt.Value)
                .Take(count)
                .ToList();

            foreach (var order in completed)
            {
                _orders.Remove(order.ClientOrderId);
                
                if (!string.IsNullOrEmpty(order.OrderId))
                {
                    _orderIdToClientId.Remove(order.OrderId);
                }
                
                _activeOrders.Remove(order.ClientOrderId);
                
                if (_symbolOrders.TryGetValue(order.Symbol, out var symbolList))
                {
                    symbolList.Remove(order.ClientOrderId);
                }
            }
        }

        public int TotalOrders => _orders.Count;
        public int ActiveCount => _activeOrders.Count;
    }
}
