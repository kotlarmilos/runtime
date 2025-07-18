// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Security.Authentication;
using System.Threading.Tasks;

using Xunit;

namespace System.Net.Security.Tests
{
    using Configuration = System.Net.Test.Common.Configuration;

    public class SslStreamDisposeTest
    {
        [Fact]
        public async Task DisposeAsync_NotConnected_ClosesStream()
        {
            bool disposed = false;
            var stream = new SslStream(new DelegateStream(disposeFunc: _ => disposed = true, canReadFunc: () => true, canWriteFunc: () => true), false, delegate { return true; });

            Assert.False(disposed);
            await stream.DisposeAsync();
            Assert.True(disposed);
        }

        [Fact]
        public async Task DisposeAsync_Connected_ClosesStream()
        {
            (Stream stream1, Stream stream2) = TestHelper.GetConnectedStreams();
            var trackingStream1 = new CallTrackingStream(stream1);
            var trackingStream2 = new CallTrackingStream(stream2);

            var clientStream = new SslStream(trackingStream1, false, delegate { return true; });
            var serverStream = new SslStream(trackingStream2, false, delegate { return true; });

            using (X509Certificate2 certificate = Configuration.Certificates.GetServerCertificate())
            {
                await TestConfiguration.WhenAllOrAnyFailedWithTimeout(
                    clientStream.AuthenticateAsClientAsync(certificate.GetNameInfo(X509NameType.SimpleName, false)),
                    serverStream.AuthenticateAsServerAsync(certificate));
            }

            Assert.Equal(0, trackingStream1.TimesCalled(nameof(Stream.DisposeAsync)));
            await clientStream.DisposeAsync();
            Assert.NotEqual(0, trackingStream1.TimesCalled(nameof(Stream.DisposeAsync)));

            Assert.Equal(0, trackingStream2.TimesCalled(nameof(Stream.DisposeAsync)));
            await serverStream.DisposeAsync();
            Assert.NotEqual(0, trackingStream2.TimesCalled(nameof(Stream.DisposeAsync)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Dispose_PendingReadAsync_ThrowsODE(bool bufferedRead)
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(TestConfiguration.PassingTestTimeout);


            (SslStream client, SslStream server) = TestHelper.GetConnectedSslStreams(leaveInnerStreamOpen: true);
            using (client)
            using (server)
            using (X509Certificate2 serverCertificate = Configuration.Certificates.GetServerCertificate())
            using (X509Certificate2 clientCertificate = Configuration.Certificates.GetClientCertificate())
            {
                SslClientAuthenticationOptions clientOptions = new SslClientAuthenticationOptions()
                {
                    TargetHost = Guid.NewGuid().ToString("N"),
                };
                clientOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

                SslServerAuthenticationOptions serverOptions = new SslServerAuthenticationOptions()
                {
                    ServerCertificate = serverCertificate,
                };

                await TestConfiguration.WhenAllOrAnyFailedWithTimeout(
                                client.AuthenticateAsClientAsync(clientOptions, default),
                                server.AuthenticateAsServerAsync(serverOptions, default));

                await TestHelper.PingPong(client, server, cts.Token);

                await server.WriteAsync("PINGPONG"u8.ToArray(), cts.Token);
                var readBuffer = new byte[1024];

                Task<int>? task = null;
                if (bufferedRead)
                {
                    // This will read everything into internal buffer. Following ReadAsync will not need IO.
                    task = client.ReadAsync(readBuffer, 0, 4, cts.Token);
                    int readLength = await task.ConfigureAwait(false);
                    client.Dispose();
                    Assert.Equal(4, readLength);
                }
                else
                {
                    client.Dispose();
                }

                await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => client.ReadAsync(readBuffer, cts.Token).AsTask());
            }
        }

        [Fact]
        [OuterLoop("Computationally expensive")]
        public async Task Dispose_ParallelWithHandshake_ThrowsODE()
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(TestConfiguration.PassingTestTimeout);

            await Parallel.ForEachAsync(System.Linq.Enumerable.Range(0, 10000), cts.Token, async (i, token) =>
            {
                (Stream clientStream, Stream serverStream) = TestHelper.GetConnectedStreams();

                using SslStream client = new SslStream(clientStream);
                using SslStream server = new SslStream(serverStream);
                using X509Certificate2 serverCertificate = Configuration.Certificates.GetServerCertificate();
                using X509Certificate2 clientCertificate = Configuration.Certificates.GetClientCertificate();

                SslClientAuthenticationOptions clientOptions = new SslClientAuthenticationOptions()
                {
                    TargetHost = Guid.NewGuid().ToString("N"),
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
                };

                SslServerAuthenticationOptions serverOptions = new SslServerAuthenticationOptions()
                {
                    ServerCertificate = serverCertificate,
                };

                var clientTask = Task.Run(() => client.AuthenticateAsClientAsync(clientOptions, cts.Token));
                var serverTask = Task.Run(() => server.AuthenticateAsServerAsync(serverOptions, cts.Token));

                // Dispose the instances while the handshake is in progress.
                client.Dispose();
                server.Dispose();

                await ValidateExceptionAsync(clientTask);
                await ValidateExceptionAsync(serverTask);
            });

            static async Task ValidateExceptionAsync(Task task)
            {
                try
                {
                    await task;
                }
                catch (InvalidOperationException ex) when (ex.StackTrace?.Contains("System.IO.StreamBuffer.WriteAsync") ?? true)
                {
                    // Writing to a disposed ConnectedStream (test only, does not happen with NetworkStream)
                    return;
                }
                catch (Exception ex) when (ex
                    is ObjectDisposedException // disposed locally
                    or IOException // disposed remotely (received unexpected EOF)
                    or AuthenticationException) // disposed wrapped in AuthenticationException or error from platform library
                {
                    // expected
                    return;
                }
            }
        }
    }
}
