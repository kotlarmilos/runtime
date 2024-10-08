// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http;
using System.Net.Test.Common;
using System.Threading;
using System.Threading.Tasks;

using Xunit;
using Xunit.Abstractions;

namespace System.Net.WebSockets.Client.Tests
{
    public static class WebSocketHelper
    {
        private static readonly Lazy<bool> s_WebSocketSupported = new Lazy<bool>(InitWebSocketSupported);
        public static bool WebSocketsSupported { get { return s_WebSocketSupported.Value; } }

        public static async Task TestEcho(
            Uri server,
            WebSocketMessageType type,
            int timeOutMilliseconds,
            ITestOutputHelper output,
            HttpMessageInvoker? invoker = null)
        {
            var cts = new CancellationTokenSource(timeOutMilliseconds);
            string message = "Hello WebSockets!";
            string closeMessage = "Good bye!";
            var receiveBuffer = new byte[100];
            var receiveSegment = new ArraySegment<byte>(receiveBuffer);

            using (ClientWebSocket cws = await GetConnectedWebSocket(server, timeOutMilliseconds, output, invoker: invoker))
            {
                output.WriteLine("TestEcho: SendAsync starting.");
                await cws.SendAsync(WebSocketData.GetBufferFromText(message), type, true, cts.Token);
                output.WriteLine("TestEcho: SendAsync done.");
                Assert.Equal(WebSocketState.Open, cws.State);

                output.WriteLine("TestEcho: ReceiveAsync starting.");
                WebSocketReceiveResult recvRet = await cws.ReceiveAsync(receiveSegment, cts.Token);
                output.WriteLine("TestEcho: ReceiveAsync done.");
                Assert.Equal(WebSocketState.Open, cws.State);
                Assert.Equal(message.Length, recvRet.Count);
                Assert.Equal(type, recvRet.MessageType);
                Assert.True(recvRet.EndOfMessage);
                Assert.Null(recvRet.CloseStatus);
                Assert.Null(recvRet.CloseStatusDescription);

                var recvSegment = new ArraySegment<byte>(receiveSegment.Array, receiveSegment.Offset, recvRet.Count);
                Assert.Equal(message, WebSocketData.GetTextFromBuffer(recvSegment));

                output.WriteLine("TestEcho: CloseAsync starting.");
                Task taskClose = cws.CloseAsync(WebSocketCloseStatus.NormalClosure, closeMessage, cts.Token);
                Assert.True(
                    (cws.State == WebSocketState.Open) || (cws.State == WebSocketState.CloseSent) ||
                    (cws.State == WebSocketState.CloseReceived) || (cws.State == WebSocketState.Closed),
                    "State immediately after CloseAsync : " + cws.State);
                await taskClose;
                output.WriteLine("TestEcho: CloseAsync done.");
                Assert.Equal(WebSocketState.Closed, cws.State);
                Assert.Equal(WebSocketCloseStatus.NormalClosure, cws.CloseStatus);
                Assert.Equal(closeMessage, cws.CloseStatusDescription);
            }
        }

        public static Task<ClientWebSocket> GetConnectedWebSocket(
            Uri server,
            int timeOutMilliseconds,
            ITestOutputHelper output,
            TimeSpan keepAliveInterval = default,
            IWebProxy proxy = null,
            HttpMessageInvoker? invoker = null) =>
                GetConnectedWebSocket(
                    server,
                    timeOutMilliseconds,
                    output,
                    options =>
                    {
                        if (proxy != null)
                        {
                            options.Proxy = proxy;
                        }
                        if (keepAliveInterval.TotalSeconds > 0)
                        {
                            options.KeepAliveInterval = keepAliveInterval;
                        }
                    },
                    invoker
                );

        public static Task<ClientWebSocket> GetConnectedWebSocket(
            Uri server,
            int timeOutMilliseconds,
            ITestOutputHelper output,
            Action<ClientWebSocketOptions> configureOptions,
            HttpMessageInvoker? invoker = null) =>
            Retry(output, async () =>
            {
                var cws = new ClientWebSocket();
                configureOptions(cws.Options);

                using (var cts = new CancellationTokenSource(timeOutMilliseconds))
                {
                    output.WriteLine("GetConnectedWebSocket: ConnectAsync starting.");
                    Task taskConnect = invoker == null ? cws.ConnectAsync(server, cts.Token) : cws.ConnectAsync(server, invoker, cts.Token);
                    Assert.True(
                        (cws.State == WebSocketState.None) ||
                        (cws.State == WebSocketState.Connecting) ||
                        (cws.State == WebSocketState.Open) ||
                        (cws.State == WebSocketState.Aborted),
                        "State immediately after ConnectAsync incorrect: " + cws.State);
                    await taskConnect;
                    output.WriteLine("GetConnectedWebSocket: ConnectAsync done.");
                    Assert.Equal(WebSocketState.Open, cws.State);
                }
                return cws;
            });

        public static async Task<T> Retry<T>(ITestOutputHelper output, Func<Task<T>> func)
        {
            const int MaxTries = 5;
            int betweenTryDelayMilliseconds = 1000;

            for (int i = 1; ; i++)
            {
                try
                {
                    return await func();
                }
                catch (WebSocketException exc)
                {
                    output.WriteLine($"Retry after attempt #{i} failed with {exc}");
                    if (i == MaxTries)
                    {
                        throw;
                    }

                    await Task.Delay(betweenTryDelayMilliseconds);
                    betweenTryDelayMilliseconds *= 2;
                }
            }
        }

        private static bool InitWebSocketSupported()
        {
            ClientWebSocket cws = null;
            if (PlatformDetection.IsBrowser && !PlatformDetection.IsWebSocketSupported)
            {
                return false;
            }

            try
            {
                cws = new ClientWebSocket();
                return true;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
            finally
            {
                if (cws != null)
                {
                    cws.Dispose();
                }
            }
        }
    }
}
