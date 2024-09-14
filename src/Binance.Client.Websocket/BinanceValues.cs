﻿using System;

namespace Binance.Client.Websocket
{
    /// <summary>
    /// Binance static urls
    /// </summary>
    public static class BinanceValues
    {
        /// <summary>
        /// Main Binance url to websocket API
        /// </summary>
        public static readonly Uri ApiWebsocketUrl = new Uri("wss://stream.binance.com:443");
        
        public static readonly Uri FuturesApiWebsocketUrl = new Uri("wss://fstream.binance.com");
    }
}
