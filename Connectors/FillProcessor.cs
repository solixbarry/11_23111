using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NYCAlphaTrader.Core;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;

namespace NYCAlphaTrader.Connectors
{
    public class FillProcessor
    {
        private readonly RiskManager _riskManager;

        public FillProcessor(RiskManager riskManager)
        {
            _riskManager = riskManager;
        }

        public async Task SubscribeFills(BinanceRestClient binance, CancellationToken token = default)
        {
            Console.WriteLine("[DEBUG] Starting SubscribeFills...");

            // 1️⃣ Subscribe to user data stream (creates listenKey + WS)
            await binance.SubscribeUserDataStream();
            Console.WriteLine("[DEBUG] Subscribed to User Data Stream");

            // 2️⃣ Loop to receive updates
            while (!token.IsCancellationRequested)
            {
                Console.WriteLine("[DEBUG] Recieving user update");
                var update = await binance.ReceiveUserUpdate();
                

                // if (update == null)
                // {
                //     // no data received, just continue
                //     Console.WriteLine("[DEBUG] no data received, just continue");
                //     await Task.Delay(100); // small delay to avoid busy loop
                //     continue;
                // }else Console.WriteLine("[DEBUG] Recieved user update");


                Console.WriteLine($"[DEBUG] Received UserDataUpdate: Type={update.Type}, Status={update.OrderStatus}, Symbol={update.Symbol}");

                // 3️⃣ Check if order was filled
                if (update.Type == "executionReport" && update.OrderStatus == "FILLED")
                {
                    Console.WriteLine("[DEBUG] Order FILLED detected");

                    var fill = new Fill
                    {
                        OrderId = update.OrderId,
                        Symbol = update.Symbol,
                        Side = update.Side == "BUY" ? Side.Buy : Side.Sell,
                        Price = update.LastExecutedPrice,
                        Quantity = update.LastExecutedQuantity,
                        Fee = update.Commission,
                        Timestamp = DateTime.UtcNow
                    };

                    // 4️⃣ Notify RiskManager
                    _riskManager.OnFill(fill);

                    // 5️⃣ Print fill details
                    Console.WriteLine($"[FILL RECEIVED] OrderId: {fill.OrderId} | Symbol: {fill.Symbol} | Side: {fill.Side} | Qty: {fill.Quantity} | Price: {fill.Price}");
                }
            }

            Console.WriteLine("[DEBUG] SubscribeFills loop exited (cancellation requested).");
        }

    }
}