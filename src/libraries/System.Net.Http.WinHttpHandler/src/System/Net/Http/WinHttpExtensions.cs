// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;

namespace System.Net.Http
{
    public static class WinHttpExtensions
    {
        public static HttpHeaders GetWinHttpTrailingHeaders(this HttpResponseMessage responseMessage)
        {
            if (responseMessage == null)
            {
                throw new ArgumentNullException(nameof(responseMessage));
            }

#if NETSTANDARD2_1
            return responseMessage.TrailingHeaders;
#else
            responseMessage.RequestMessage.Properties.TryGetValue("__ResponseTrailers", out object trailers);
            return (HttpHeaders)trailers;
#endif
        }
    }
}
