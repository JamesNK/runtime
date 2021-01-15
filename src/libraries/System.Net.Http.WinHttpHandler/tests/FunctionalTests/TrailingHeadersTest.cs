// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Functional.Tests;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Test.Common;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace System.Net.Http.WinHttpHandlerFunctional.Tests
{
    public class TrailingHeadersTest : HttpClientHandlerTestBase
    {
        public TrailingHeadersTest(ITestOutputHelper output) : base(output)
        { }

        protected override Version UseVersion => new Version(2, 0);

        protected static byte[] DataBytes = Encoding.ASCII.GetBytes("data");

        protected static readonly IList<HttpHeaderData> TrailingHeaders = new HttpHeaderData[] {
            new HttpHeaderData("MyCoolTrailerHeader", "amazingtrailer"),
            new HttpHeaderData("EmptyHeader", ""),
            new HttpHeaderData("Accept-Encoding", "identity,gzip"),
            new HttpHeaderData("Hello", "World") };

        protected static Frame MakeDataFrame(int streamId, byte[] data, bool endStream = false) =>
            new DataFrame(data, (endStream ? FrameFlags.EndStream : FrameFlags.None), 0, streamId);

        [ConditionalFact(typeof(PlatformDetection), nameof(PlatformDetection.SupportsAlpn))]
        public async Task BidirectionalStreaming()
        {
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (Http2LoopbackServer server = Http2LoopbackServer.CreateServer())
            using (HttpClient client = CreateHttpClient())
            {
                HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, server.Address);
                message.Version = new Version(2, 0);
                message.Content = new StreamingContent(async s =>
                {
                    await s.WriteAsync(new byte[50]);

                    await tcs.Task;

                    await s.WriteAsync(new byte[50]);
                }, 100);

                Task<HttpResponseMessage> sendTask = client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);

                Http2LoopbackConnection connection = await server.EstablishConnectionAsync();

                int streamId = await connection.ReadRequestHeaderAsync(expectEndOfStream: false);

                var frame = await connection.ReadDataFrameAsync();

                // Response header.
                await connection.SendDefaultResponseHeadersAsync(streamId);

                // Response data.
                await connection.WriteFrameAsync(MakeDataFrame(streamId, DataBytes, endStream: false));

                // Server doesn't send trailing header frame.
                HttpResponseMessage response = await sendTask;
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var responseStream = await response.Content.ReadAsStreamAsync();

                var buffer = new byte[1024];
                var readCount = await responseStream.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(DataBytes.Length, readCount);

                tcs.SetResult(null);

                frame = await connection.ReadDataFrameAsync();

                // Response data.
                await connection.WriteFrameAsync(MakeDataFrame(streamId, DataBytes, endStream: true));

                readCount = await responseStream.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(DataBytes.Length, readCount);

                readCount = await responseStream.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(0, readCount);

                //var trailingHeaders = response.GetWinHttpTrailingHeaders();
                //Assert.NotNull(trailingHeaders);
                //Assert.Equal(0, trailingHeaders.Count());
            }
        }

        private class StreamingContent : HttpContent
        {
            private readonly Func<Stream, Task> _writeFunc;
            private readonly long _length;

            public StreamingContent(Func<Stream, Task> writeFunc, long length)
            {
                _writeFunc = writeFunc;
                _length = length;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                return _writeFunc(stream);
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _length;
                return true;
            }
        }

        [ConditionalFact(typeof(PlatformDetection), nameof(PlatformDetection.SupportsAlpn))]
        public async Task Http2GetAsync_NoTrailingHeaders_EmptyCollection()
        {
            using (Http2LoopbackServer server = Http2LoopbackServer.CreateServer())
            using (HttpClient client = CreateHttpClient())
            {
                Task<HttpResponseMessage> sendTask = client.GetAsync(server.Address);

                Http2LoopbackConnection connection = await server.EstablishConnectionAsync();

                int streamId = await connection.ReadRequestHeaderAsync();

                // Response header.
                await connection.SendDefaultResponseHeadersAsync(streamId);

                // Response data.
                await connection.WriteFrameAsync(MakeDataFrame(streamId, DataBytes, endStream: true));

                // Server doesn't send trailing header frame.
                HttpResponseMessage response = await sendTask;
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var trailingHeaders = response.GetWinHttpTrailingHeaders();
                Assert.NotNull(trailingHeaders);
                Assert.Equal(0, trailingHeaders.Count());
            }
        }

        [ConditionalFact(typeof(PlatformDetection), nameof(PlatformDetection.SupportsAlpn))]
        public async Task Http2GetAsync_MissingTrailer_TrailingHeadersAccepted()
        {
            using (Http2LoopbackServer server = Http2LoopbackServer.CreateServer())
            using (HttpClient client = CreateHttpClient())
            {
                Task<HttpResponseMessage> sendTask = client.GetAsync(server.Address);

                Http2LoopbackConnection connection = await server.EstablishConnectionAsync();

                int streamId = await connection.ReadRequestHeaderAsync();

                // Response header.
                await connection.SendDefaultResponseHeadersAsync(streamId);

                // Response data, missing Trailers.
                await connection.WriteFrameAsync(MakeDataFrame(streamId, DataBytes));

                // Additional trailing header frame.
                await connection.SendResponseHeadersAsync(streamId, isTrailingHeader: true, headers: TrailingHeaders, endStream: true);

                HttpResponseMessage response = await sendTask;
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var trailingHeaders = response.GetWinHttpTrailingHeaders();
                Assert.Equal(TrailingHeaders.Count, trailingHeaders.Count());
                Assert.Contains("amazingtrailer", trailingHeaders.GetValues("MyCoolTrailerHeader"));
                Assert.Contains("World", trailingHeaders.GetValues("Hello"));
            }
        }

        [ConditionalFact(typeof(PlatformDetection), nameof(PlatformDetection.SupportsAlpn))]
        public async Task Http2GetAsyncResponseHeadersReadOption_TrailingHeaders_Available()
        {
            using (Http2LoopbackServer server = Http2LoopbackServer.CreateServer())
            using (HttpClient client = CreateHttpClient())
            {
                Task<HttpResponseMessage> sendTask = client.GetAsync(server.Address, HttpCompletionOption.ResponseHeadersRead);

                Http2LoopbackConnection connection = await server.EstablishConnectionAsync();

                int streamId = await connection.ReadRequestHeaderAsync();

                // Response header.
                await connection.SendDefaultResponseHeadersAsync(streamId);

                // Response data, missing Trailers.
                await connection.WriteFrameAsync(MakeDataFrame(streamId, DataBytes));

                HttpResponseMessage response = await sendTask;
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                // Pending read on the response content.
                var trailingHeaders = response.GetWinHttpTrailingHeaders();
                Assert.True(trailingHeaders == null || trailingHeaders.Count() == 0);

                Stream stream = await response.Content.ReadAsStreamAsync(TestAsync);
                Byte[] data = new Byte[100];
                await stream.ReadAsync(data, 0, data.Length);

                // Intermediate test - haven't reached stream EOF yet.
                trailingHeaders = response.GetWinHttpTrailingHeaders();
                Assert.True(trailingHeaders == null || trailingHeaders.Count() == 0);

                // Finish data stream and write out trailing headers.
                await connection.WriteFrameAsync(MakeDataFrame(streamId, DataBytes));
                await connection.SendResponseHeadersAsync(streamId, endStream: true, isTrailingHeader: true, headers: TrailingHeaders);

                // Read data until EOF is reached
                while (stream.Read(data, 0, data.Length) != 0) ;

                trailingHeaders = response.GetWinHttpTrailingHeaders();
                Assert.Equal(TrailingHeaders.Count, trailingHeaders.Count());
                Assert.Contains("amazingtrailer", trailingHeaders.GetValues("MyCoolTrailerHeader"));
                Assert.Contains("World", trailingHeaders.GetValues("Hello"));

                // Read when already zero. Trailers shouldn't be changed.
                stream.Read(data, 0, data.Length);

                trailingHeaders = response.GetWinHttpTrailingHeaders();
                Assert.Equal(TrailingHeaders.Count, trailingHeaders.Count());
            }
        }

        [ConditionalFact(typeof(PlatformDetection), nameof(PlatformDetection.SupportsAlpn))]
        public async Task Http2GetAsync_TrailerHeaders_TrailingHeaderNoBody()
        {
            using (Http2LoopbackServer server = Http2LoopbackServer.CreateServer())
            using (HttpClient client = CreateHttpClient())
            {
                Task<HttpResponseMessage> sendTask = client.GetAsync(server.Address);

                Http2LoopbackConnection connection = await server.EstablishConnectionAsync();

                int streamId = await connection.ReadRequestHeaderAsync();

                // Response header.
                await connection.SendDefaultResponseHeadersAsync(streamId);
                await connection.SendResponseHeadersAsync(streamId, endStream: true, isTrailingHeader: true, headers: TrailingHeaders);

                HttpResponseMessage response = await sendTask;
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var trailingHeaders = response.GetWinHttpTrailingHeaders();
                Assert.Equal(TrailingHeaders.Count, trailingHeaders.Count());
                Assert.Contains("amazingtrailer", trailingHeaders.GetValues("MyCoolTrailerHeader"));
                Assert.Contains("World", trailingHeaders.GetValues("Hello"));
            }
        }

        [ConditionalFact(typeof(PlatformDetection), nameof(PlatformDetection.SupportsAlpn))]
        public async Task Http2GetAsync_TrailingHeaders_NoData_EmptyResponseObserved()
        {
            using (Http2LoopbackServer server = Http2LoopbackServer.CreateServer())
            using (HttpClient client = CreateHttpClient())
            {
                Task<HttpResponseMessage> sendTask = client.GetAsync(server.Address);

                Http2LoopbackConnection connection = await server.EstablishConnectionAsync();

                int streamId = await connection.ReadRequestHeaderAsync();

                // Response header.
                await connection.SendDefaultResponseHeadersAsync(streamId);

                // No data.

                // Response trailing headers
                await connection.SendResponseHeadersAsync(streamId, isTrailingHeader: true, headers: TrailingHeaders);

                HttpResponseMessage response = await sendTask;
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal<byte>(Array.Empty<byte>(), await response.Content.ReadAsByteArrayAsync());

                var trailingHeaders = response.GetWinHttpTrailingHeaders();
                Assert.Contains("amazingtrailer", trailingHeaders.GetValues("MyCoolTrailerHeader"));
                Assert.Contains("World", trailingHeaders.GetValues("Hello"));
            }
        }
    }
}
