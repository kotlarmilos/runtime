// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.IO;
using System.Net.Http.Metrics;
using System.Net.Security;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    [UnsupportedOSPlatform("browser")]
    public sealed class SocketsHttpHandler : HttpMessageHandler
    {
        private readonly HttpConnectionSettings _settings = new HttpConnectionSettings();
        private HttpMessageHandlerStage? _handler;
        private Task<HttpMessageHandlerStage>? _handlerChainSetupTask;
        private Func<HttpConnectionSettings, HttpMessageHandlerStage, HttpMessageHandlerStage>? _decompressionHandlerFactory;
        private bool _disposed;

        // Accessed via UnsafeAccessor from HttpWebRequest.
        internal HttpConnectionSettings Settings => _settings;

        private void CheckDisposedOrStarted()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_handler != null)
            {
                throw new InvalidOperationException(SR.net_http_operation_started);
            }
        }

        /// <summary>
        /// Gets a value that indicates whether the handler is supported on the current platform.
        /// </summary>
        [UnsupportedOSPlatformGuard("browser")]
        public static bool IsSupported => !OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi();

        public bool UseCookies
        {
            get => _settings._useCookies;
            set
            {
                CheckDisposedOrStarted();
                _settings._useCookies = value;
            }
        }

        [AllowNull]
        public CookieContainer CookieContainer
        {
            get => _settings._cookieContainer ??= new CookieContainer();
            set
            {
                CheckDisposedOrStarted();
                _settings._cookieContainer = value;
            }
        }

        public DecompressionMethods AutomaticDecompression
        {
            get => _settings._automaticDecompression;
            set
            {
                CheckDisposedOrStarted();
                EnsureDecompressionHandlerFactory();
                _settings._automaticDecompression = value;
            }
        }

        public bool UseProxy
        {
            get => _settings._useProxy;
            set
            {
                CheckDisposedOrStarted();
                _settings._useProxy = value;
            }
        }

        public IWebProxy? Proxy
        {
            get => _settings._proxy;
            set
            {
                CheckDisposedOrStarted();
                _settings._proxy = value;
            }
        }

        public ICredentials? DefaultProxyCredentials
        {
            get => _settings._defaultProxyCredentials;
            set
            {
                CheckDisposedOrStarted();
                _settings._defaultProxyCredentials = value;
            }
        }

        public bool PreAuthenticate
        {
            get => _settings._preAuthenticate;
            set
            {
                CheckDisposedOrStarted();
                _settings._preAuthenticate = value;
            }
        }

        public ICredentials? Credentials
        {
            get => _settings._credentials;
            set
            {
                CheckDisposedOrStarted();
                _settings._credentials = value;
            }
        }

        public bool AllowAutoRedirect
        {
            get => _settings._allowAutoRedirect;
            set
            {
                CheckDisposedOrStarted();
                _settings._allowAutoRedirect = value;
            }
        }

        public int MaxAutomaticRedirections
        {
            get => _settings._maxAutomaticRedirections;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

                CheckDisposedOrStarted();
                _settings._maxAutomaticRedirections = value;
            }
        }

        public int MaxConnectionsPerServer
        {
            get => _settings._maxConnectionsPerServer;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

                CheckDisposedOrStarted();
                _settings._maxConnectionsPerServer = value;
            }
        }

        public int MaxResponseDrainSize
        {
            get => _settings._maxResponseDrainSize;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);

                CheckDisposedOrStarted();
                _settings._maxResponseDrainSize = value;
            }
        }

        public TimeSpan ResponseDrainTimeout
        {
            get => _settings._maxResponseDrainTime;
            set
            {
                if ((value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan) ||
                    (value.TotalMilliseconds > int.MaxValue))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                CheckDisposedOrStarted();
                _settings._maxResponseDrainTime = value;
            }
        }

        public int MaxResponseHeadersLength
        {
            get => _settings._maxResponseHeadersLength;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

                CheckDisposedOrStarted();
                _settings._maxResponseHeadersLength = value;
            }
        }

        [AllowNull]
        public SslClientAuthenticationOptions SslOptions
        {
            get => _settings._sslOptions ??= new SslClientAuthenticationOptions();
            set
            {
                CheckDisposedOrStarted();
                _settings._sslOptions = value;
            }
        }

        public TimeSpan PooledConnectionLifetime
        {
            get => _settings._pooledConnectionLifetime;
            set
            {
                if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                CheckDisposedOrStarted();
                _settings._pooledConnectionLifetime = value;
            }
        }

        public TimeSpan PooledConnectionIdleTimeout
        {
            get => _settings._pooledConnectionIdleTimeout;
            set
            {
                if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                CheckDisposedOrStarted();
                _settings._pooledConnectionIdleTimeout = value;
            }
        }

        public TimeSpan ConnectTimeout
        {
            get => _settings._connectTimeout;
            set
            {
                if ((value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan) ||
                    (value.TotalMilliseconds > int.MaxValue))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                CheckDisposedOrStarted();
                _settings._connectTimeout = value;
            }
        }

        public TimeSpan Expect100ContinueTimeout
        {
            get => _settings._expect100ContinueTimeout;
            set
            {
                if ((value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan) ||
                    (value.TotalMilliseconds > int.MaxValue))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                CheckDisposedOrStarted();
                _settings._expect100ContinueTimeout = value;
            }
        }

        /// <summary>
        /// Defines the initial HTTP2 stream receive window size for all connections opened by the this <see cref="SocketsHttpHandler"/>.
        /// </summary>
        /// <remarks>
        /// Larger the values may lead to faster download speed, but potentially higher memory footprint.
        /// The property must be set to a value between 65535 and the configured maximum window size, which is 16777216 by default.
        /// </remarks>
        public int InitialHttp2StreamWindowSize
        {
            get => _settings._initialHttp2StreamWindowSize;
            set
            {
                if (value < HttpHandlerDefaults.DefaultInitialHttp2StreamWindowSize || value > GlobalHttpSettings.SocketsHttpHandler.MaxHttp2StreamWindowSize)
                {
                    string message = SR.Format(
                        SR.net_http_http2_invalidinitialstreamwindowsize,
                        HttpHandlerDefaults.DefaultInitialHttp2StreamWindowSize,
                        GlobalHttpSettings.SocketsHttpHandler.MaxHttp2StreamWindowSize);

                    throw new ArgumentOutOfRangeException(nameof(InitialHttp2StreamWindowSize), message);
                }
                CheckDisposedOrStarted();
                _settings._initialHttp2StreamWindowSize = value;
            }
        }

        /// <summary>
        /// Gets or sets the keep alive ping delay. The client will send a keep alive ping to the server if it
        /// doesn't receive any frames on a connection for this period of time. This property is used together with
        /// <see cref="SocketsHttpHandler.KeepAlivePingTimeout"/> to close broken connections.
        /// <para>
        /// Delay value must be greater than or equal to 1 second. Set to <see cref="Timeout.InfiniteTimeSpan"/> to
        /// disable the keep alive ping.
        /// Defaults to <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </para>
        /// </summary>
        public TimeSpan KeepAlivePingDelay
        {
            get => _settings._keepAlivePingDelay;
            set
            {
                if (value.Ticks < TimeSpan.TicksPerSecond && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, SR.Format(SR.net_http_value_must_be_greater_than_or_equal, value, TimeSpan.FromSeconds(1)));
                }

                CheckDisposedOrStarted();
                _settings._keepAlivePingDelay = value;
            }
        }

        /// <summary>
        /// Gets or sets the keep alive ping timeout. Keep alive pings are sent when a period of inactivity exceeds
        /// the configured <see cref="KeepAlivePingDelay"/> value. The client will close the connection if it
        /// doesn't receive any frames within the timeout.
        /// <para>
        /// Timeout must be greater than or equal to 1 second. Set to <see cref="Timeout.InfiniteTimeSpan"/> to
        /// disable the keep alive ping timeout.
        /// Defaults to 20 seconds.
        /// </para>
        /// </summary>
        public TimeSpan KeepAlivePingTimeout
        {
            get => _settings._keepAlivePingTimeout;
            set
            {
                if (value.Ticks < TimeSpan.TicksPerSecond && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, SR.Format(SR.net_http_value_must_be_greater_than_or_equal, value, TimeSpan.FromSeconds(1)));
                }

                CheckDisposedOrStarted();
                _settings._keepAlivePingTimeout = value;
            }
        }

        /// <summary>
        /// Gets or sets the keep alive ping behaviour. Keep alive pings are sent when a period of inactivity exceeds
        /// the configured <see cref="KeepAlivePingDelay"/> value.
        /// </summary>
        public HttpKeepAlivePingPolicy KeepAlivePingPolicy
        {
            get => _settings._keepAlivePingPolicy;
            set
            {
                CheckDisposedOrStarted();
                _settings._keepAlivePingPolicy = value;
            }
        }

        /// <summary>
        /// Gets or sets a value that indicates whether additional HTTP/2 connections can be established to the same server.
        /// </summary>
        /// <remarks>
        /// Enabling multiple connections to the same server explicitly goes against <see href="https://www.rfc-editor.org/rfc/rfc9113.html#section-9.1-2">RFC 9113 - HTTP/2</see>.
        /// </remarks>
        public bool EnableMultipleHttp2Connections
        {
            get => _settings._enableMultipleHttp2Connections;
            set
            {
                CheckDisposedOrStarted();

                _settings._enableMultipleHttp2Connections = value;
            }
        }

        /// <summary>
        /// Gets or sets a value that indicates whether additional HTTP/3 connections can be established to the same server.
        /// </summary>
        /// <remarks>
        /// Enabling multiple connections to the same server explicitly goes against <see href="https://www.rfc-editor.org/rfc/rfc9114.html#section-3.3-4">RFC 9114 - HTTP/3</see>.
        /// </remarks>
        public bool EnableMultipleHttp3Connections
        {
            get => _settings._enableMultipleHttp3Connections;
            set
            {
                CheckDisposedOrStarted();

                _settings._enableMultipleHttp3Connections = value;
            }
        }

        internal const bool SupportsAutomaticDecompression = true;
        internal const bool SupportsProxy = true;
        internal const bool SupportsRedirectConfiguration = true;

        /// <summary>
        /// When non-null, a custom callback used to open new connections.
        /// </summary>
        public Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>? ConnectCallback
        {
            get => _settings._connectCallback;
            set
            {
                CheckDisposedOrStarted();
                _settings._connectCallback = value;
            }
        }

        /// <summary>
        /// Gets or sets a custom callback that provides access to the plaintext HTTP protocol stream.
        /// </summary>
        public Func<SocketsHttpPlaintextStreamFilterContext, CancellationToken, ValueTask<Stream>>? PlaintextStreamFilter
        {
            get => _settings._plaintextStreamFilter;
            set
            {
                CheckDisposedOrStarted();
                _settings._plaintextStreamFilter = value;
            }
        }

        /// <summary>
        /// Gets a writable dictionary (that is, a map) of custom properties for the HttpClient requests. The dictionary is initialized empty; you can insert and query key-value pairs for your custom handlers and special processing.
        /// </summary>
        public IDictionary<string, object?> Properties =>
            _settings._properties ??= new Dictionary<string, object?>();

        /// <summary>
        /// Gets or sets a callback that returns the <see cref="Encoding"/> to encode the value for the specified request header name,
        /// or <see langword="null"/> to use the default behavior.
        /// </summary>
        public HeaderEncodingSelector<HttpRequestMessage>? RequestHeaderEncodingSelector
        {
            get => _settings._requestHeaderEncodingSelector;
            set
            {
                CheckDisposedOrStarted();
                _settings._requestHeaderEncodingSelector = value;
            }
        }

        /// <summary>
        /// Gets or sets a callback that returns the <see cref="Encoding"/> to decode the value for the specified response header name,
        /// or <see langword="null"/> to use the default behavior.
        /// </summary>
        public HeaderEncodingSelector<HttpRequestMessage>? ResponseHeaderEncodingSelector
        {
            get => _settings._responseHeaderEncodingSelector;
            set
            {
                CheckDisposedOrStarted();
                _settings._responseHeaderEncodingSelector = value;
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="DistributedContextPropagator"/> to use when propagating the distributed trace and context.
        /// Use <see langword="null"/> to disable propagation.
        /// Defaults to <see cref="DistributedContextPropagator.Current"/>.
        /// </summary>
        [CLSCompliant(false)]
        public DistributedContextPropagator? ActivityHeadersPropagator
        {
            get => _settings._activityHeadersPropagator;
            set
            {
                CheckDisposedOrStarted();
                _settings._activityHeadersPropagator = value;
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="IMeterFactory"/> to create a custom <see cref="Meter"/> for the <see cref="SocketsHttpHandler"/> instance.
        /// </summary>
        /// <remarks>
        /// When <see cref="MeterFactory"/> is set to a non-<see langword="null"/> value, all metrics emitted by the <see cref="SocketsHttpHandler"/> instance
        /// will be recorded using the <see cref="Meter"/> provided by the <see cref="IMeterFactory"/>.
        /// </remarks>
        [CLSCompliant(false)]
        public IMeterFactory? MeterFactory
        {
            get => _settings._meterFactory;
            set
            {
                CheckDisposedOrStarted();
                _settings._meterFactory = value;
            }
        }

        internal ClientCertificateOption ClientCertificateOptions
        {
            get => _settings._clientCertificateOptions;
            set
            {
                CheckDisposedOrStarted();
                _settings._clientCertificateOptions = value;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _handler?.Dispose();
            }

            base.Dispose(disposing);
        }

        private HttpMessageHandlerStage SetupHandlerChain()
        {
            // Clone the settings to get a relatively consistent view that won't change after this point.
            // (This isn't entirely complete, as some of the collections it contains aren't currently deeply cloned.)
            HttpConnectionSettings settings = _settings.CloneAndNormalize();

            HttpConnectionPoolManager poolManager = new HttpConnectionPoolManager(settings);
            HttpMessageHandlerStage handler = new HttpConnectionHandler(poolManager, doRequestAuth: settings._credentials is { });

            // MetricsHandler should be descendant of DiagnosticsHandler in the handler chain to make sure the 'http.request.duration'
            // metric is recorded before stopping the request Activity. This is needed to make sure that our telemetry supports Exemplars.
            if (GlobalHttpSettings.MetricsHandler.IsGloballyEnabled)
            {
                handler = new MetricsHandler(handler, settings._meterFactory, settings._proxy, out Meter meter);
                settings._metrics = new SocketsHttpHandlerMetrics(meter);
            }

            // DiagnosticsHandler is inserted before RedirectHandler so that trace propagation is done on redirects as well
            if (GlobalHttpSettings.DiagnosticsHandler.EnableActivityPropagation && settings._activityHeadersPropagator is DistributedContextPropagator propagator)
            {
                handler = new DiagnosticsHandler(handler, propagator, settings._proxy, settings._allowAutoRedirect);
            }

            if (settings._allowAutoRedirect)
            {
                // Just as with WinHttpHandler, for security reasons, we do not support authentication on redirects
                // if the credential is anything other than a CredentialCache.
                // We allow credentials in a CredentialCache since they are specifically tied to URIs.
                handler = new RedirectHandler(settings._maxAutomaticRedirections, handler, disableAuthOnRedirect: settings._credentials is not CredentialCache);
            }

            if (settings._automaticDecompression != DecompressionMethods.None)
            {
                Debug.Assert(_decompressionHandlerFactory is not null);
                handler = _decompressionHandlerFactory(settings, handler);
            }

            // Ensure a single handler is used for all requests.
            if (Interlocked.CompareExchange(ref _handler, handler, null) != null)
            {
                handler.Dispose();
            }

            return _handler;
        }

        // Allows for DecompressionHandler (and its compression dependencies) to be trimmed when
        // AutomaticDecompression is not being used.
        private void EnsureDecompressionHandlerFactory()
        {
            _decompressionHandlerFactory ??= (settings, handler) => new DecompressionHandler(settings._automaticDecompression, handler);
        }

        protected internal override HttpResponseMessage Send(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Version.Major >= 2)
            {
                throw new NotSupportedException(SR.Format(SR.net_http_http2_sync_not_supported, GetType()));
            }

            // Do not allow upgrades for synchronous requests, that might lead to asynchronous code-paths.
            if (request.VersionPolicy == HttpVersionPolicy.RequestVersionOrHigher)
            {
                throw new NotSupportedException(SR.Format(SR.net_http_upgrade_not_enabled_sync, nameof(Send), request.VersionPolicy));
            }

            ObjectDisposedException.ThrowIf(_disposed, this);

            cancellationToken.ThrowIfCancellationRequested();

            Exception? error = ValidateAndNormalizeRequest(request);
            if (error != null)
            {
                throw error;
            }

            HttpMessageHandlerStage handler = _handler ?? SetupHandlerChain();

            return handler.Send(request, cancellationToken);
        }

        protected internal override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            ObjectDisposedException.ThrowIf(_disposed, this);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            }

            Exception? error = ValidateAndNormalizeRequest(request);
            if (error != null)
            {
                return Task.FromException<HttpResponseMessage>(error);
            }

            return _handler is { } handler
                ? handler.SendAsync(request, cancellationToken)
                : CreateHandlerAndSendAsync(request, cancellationToken);

            // SetupHandlerChain may block for a few seconds in some environments.
            // E.g. during the first access of HttpClient.DefaultProxy - https://github.com/dotnet/runtime/issues/115301.
            // The setup procedure is enqueued to thread pool to prevent the caller from blocking.
            async Task<HttpResponseMessage> CreateHandlerAndSendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _handlerChainSetupTask ??= Task.Run(SetupHandlerChain);
                HttpMessageHandlerStage handler = await _handlerChainSetupTask.ConfigureAwait(false);
                return await handler.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        private static Exception? ValidateAndNormalizeRequest(HttpRequestMessage request)
        {
            if (request.Version != HttpVersion.Version10 && request.Version != HttpVersion.Version11 && request.Version != HttpVersion.Version20 && request.Version != HttpVersion.Version30)
            {
                return ExceptionDispatchInfo.SetCurrentStackTrace(new NotSupportedException(SR.net_http_unsupported_version));
            }

            // Add headers to define content transfer, if not present
            if (request.HasHeaders && request.Headers.TransferEncodingChunked.GetValueOrDefault())
            {
                if (request.Content == null)
                {
                    return ExceptionDispatchInfo.SetCurrentStackTrace(new HttpRequestException(SR.net_http_client_execution_error,
                        ExceptionDispatchInfo.SetCurrentStackTrace(new InvalidOperationException(SR.net_http_chunked_not_allowed_with_empty_content))));
                }

                // Since the user explicitly set TransferEncodingChunked to true, we need to remove
                // the Content-Length header if present, as sending both is invalid.
                request.Content.Headers.ContentLength = null;
            }
            else if (request.Content != null && request.Content.Headers.ContentLength == null)
            {
                // We have content, but neither Transfer-Encoding nor Content-Length is set.
                request.Headers.TransferEncodingChunked = true;
            }

            if (request.Version.Minor == 0 && request.Version.Major == 1 && request.HasHeaders)
            {
                // HTTP 1.0 does not support chunking
                if (request.Headers.TransferEncodingChunked == true)
                {
                    return ExceptionDispatchInfo.SetCurrentStackTrace(new NotSupportedException(SR.net_http_unsupported_chunking));
                }

                // HTTP 1.0 does not support Expect: 100-continue; just disable it.
                if (request.Headers.ExpectContinue == true)
                {
                    request.Headers.ExpectContinue = false;
                }
            }

            Uri? requestUri = request.RequestUri;
            if (requestUri is null || !requestUri.IsAbsoluteUri)
            {
                return ExceptionDispatchInfo.SetCurrentStackTrace(new InvalidOperationException(SR.net_http_client_invalid_requesturi));
            }

            if (!HttpUtilities.IsSupportedScheme(requestUri.Scheme))
            {
                return ExceptionDispatchInfo.SetCurrentStackTrace(new NotSupportedException(SR.Format(SR.net_http_unsupported_requesturi_scheme, requestUri.Scheme)));
            }

            return null;
        }
    }
}
