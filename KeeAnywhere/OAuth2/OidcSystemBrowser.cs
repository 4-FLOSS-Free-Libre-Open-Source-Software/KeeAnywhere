﻿using IdentityModel.OidcClient.Browser;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KeeAnywhere.OAuth2
{
    public class OidcSystemBrowser : IBrowser
    {
        public int Port { get; }
        private readonly string _path;

        public OidcSystemBrowser(int? port = null, string path = null)
        {
            _path = path;

            if (!port.HasValue)
            {
                Port = GetUnusedPort();
            }
            else
            {
                Port = port.Value;
            }
        }

        public static int GetUnusedPort()
        {
            //var listener = new TcpListener(IPAddress.Loopback, 0);
            //listener.Start();
            //var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            //listener.Stop();
            //return port;

            var startingPort = 50001;
            var maxCount = 5;

            for (var i = 0; i < maxCount; i++)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, startingPort);
                    listener.Start();
                    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    listener.Stop();
                    return port;
                }
                catch
                {
                    startingPort++;
                }
            }

            throw new Exception("No unused prot found.");
        }

        public string RedirectUri
        {
            get
            {
                return $"http://127.0.0.1:{Port}/{_path}";
            }
        }

        internal const string DefaultClosePageResponse =
    @"<html>
          <head><title>OAuth 2.0 Authentication Token Received</title></head>
          <body>
            Received verification code. You may now close this window.
            <script type='text/javascript'>
              // This doesn't work on every browser.
              window.setTimeout(function() {
                  this.focus();
                  window.opener = this;
                  window.open('', '_self', ''); 
                  window.close(); 
                }, 1000);
              //if (window.opener) { window.opener.checkToken(); }
            </script>
          </body>
        </html>";

        public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken)
        {
            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(RedirectUri);
                listener.Start();

                OpenBrowser(options.StartUrl);

                try
                {
                    var context = await listener.GetContextAsync();

                    string result;

                    if (options.ResponseMode == IdentityModel.OidcClient.OidcClientOptions.AuthorizeResponseMode.Redirect)
                    {
                        result = context.Request.Url.Query;
                    }
                    else
                    {
                        result = ProcessFormPost(context.Request);
                    }

                    await SendResponse(context.Response);


                    if (String.IsNullOrWhiteSpace(result))
                    {
                        return new BrowserResult { ResultType = BrowserResultType.UnknownError, Error = "Empty response." };
                    }

                    return new BrowserResult { Response = result, ResultType = BrowserResultType.Success };
                }
                catch (TaskCanceledException ex)
                {
                    return new BrowserResult { ResultType = BrowserResultType.Timeout, Error = ex.Message };
                }
                catch (Exception ex)
                {
                    return new BrowserResult { ResultType = BrowserResultType.UnknownError, Error = ex.Message };
                }
            }
        }

        public async Task<NameValueCollection> GetQueryStringAsync(string startUrl, CancellationToken cancellationToken)
        {
            using (var listener = new HttpListener())
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    listener.Prefixes.Add(RedirectUri);
                    listener.Start();

                    OpenBrowser(startUrl);

                    //try
                    //{
                    var context = await listener.GetContextAsync();

                    var result = context.Request.QueryString;

                    await SendResponse(context.Response);

                    return result;
                }
                catch (TaskCanceledException ex)
                {
                    return null;
                }
                //catch (Exception ex)
                //{
                //    return null;
            }
        }

        private static async Task SendResponse(HttpListenerResponse response)
        {
            var buffer = Encoding.UTF8.GetBytes(DefaultClosePageResponse);
            response.ContentLength64 = buffer.Length;
            var responseOutput = response.OutputStream;
            await responseOutput.WriteAsync(buffer, 0, buffer.Length);
            responseOutput.Close();
        }

        private static string ProcessFormPost(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
            {
                return null;
            }

            using (var body = request.InputStream)
            {
                using (var reader = new StreamReader(body, request.ContentEncoding))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}

