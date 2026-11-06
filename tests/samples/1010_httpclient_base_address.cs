// HttpClient.BaseAddress with relative request URIs, relative Location headers of redirects (resolved against the
// request URI), query strings and escaped characters, over plain HTTP/1.1 against a loopback server.
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;

_ = Task.Run(async () =>
{
    while (true)
    {
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(); }
        catch (Exception) { return; }
        _ = Task.Run(async () =>
        {
            using var tcp = client;
            var stream = tcp.GetStream();
            var buffer = new byte[4096];
            var request = new StringBuilder();
            while (true)
            {
                int n = await stream.ReadAsync(buffer);
                if (n == 0) return;
                request.Append(Encoding.ASCII.GetString(buffer, 0, n));
                if (!request.ToString().Contains("\r\n\r\n")) continue;
                string target = request.ToString().Split(' ')[1];
                request.Clear();
                string response = target switch
                {
                    "/api/redirect" => "HTTP/1.1 302 Found\r\nLocation: ../moved/here?from=redirect\r\nContent-Length: 0\r\n\r\n",
                    "/api/root-redirect" => "HTTP/1.1 301 Moved Permanently\r\nLocation: /top\r\nContent-Length: 0\r\n\r\n",
                    _ => "HTTP/1.1 200 OK\r\nContent-Length: " + target.Length + "\r\n\r\n" + target,
                };
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
            }
        });
    }
});

using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port + "/api/") };
Console.WriteLine(http.BaseAddress!.ToString().Replace(port.ToString(), "PORT"));
foreach (string relative in new[] { "items/1", "./x/../y", "/absolute/path", "nested/dir/file.txt" })
    Console.WriteLine(relative + " -> " + await http.GetStringAsync(relative));
Console.WriteLine("Uri overload: " + await http.GetStringAsync(new Uri("by-uri", UriKind.Relative)));
Console.WriteLine("absolute wins: " + await http.GetStringAsync("http://127.0.0.1:" + port + "/elsewhere"));

using (var response = await http.GetAsync("redirect"))
    Console.WriteLine("relative redirect: " + await response.Content.ReadAsStringAsync() + " final " + response.RequestMessage!.RequestUri!.PathAndQuery);
using (var response = await http.GetAsync("root-redirect"))
    Console.WriteLine("root redirect: " + await response.Content.ReadAsStringAsync());

using var plain = new HttpClient();
try { await plain.GetStringAsync("relative/only"); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
listener.Stop();
