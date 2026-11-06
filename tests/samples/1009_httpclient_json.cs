// System.Net.Http.Json over HttpClient against a loopback server: GetFromJsonAsync, PostAsJsonAsync, ReadFromJsonAsync
// and JsonContent.Create with a source-generated JsonSerializerContext (what a trimmed or AOT program uses),
// the request body the server receives, a response body in UTF-16 (transcoded) and the failure modes (a non-JSON
// content type, an error status, malformed JSON).
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;

async Task Serve()
{
    while (true)
    {
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(); }
        catch (Exception) { return; }
        _ = Task.Run(() => Handle(client));
    }
}

async Task Handle(TcpClient client)
{
    using var tcp = client;
    var stream = tcp.GetStream();
    var buffer = new byte[16384];
    var pending = new List<byte>();
    while (true)
    {
        int end;
        while ((end = Find(pending, "\r\n\r\n")) < 0)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) return;
            pending.AddRange(buffer.AsSpan(0, n).ToArray());
        }
        var lines = Encoding.ASCII.GetString(pending.GetRange(0, end).ToArray()).Split("\r\n");
        pending.RemoveRange(0, end + 4);
        string path = lines[0].Split(' ')[1];
        int length = lines.Skip(1).Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Select(l => int.Parse(l[15..])).FirstOrDefault();
        string contentType = lines.Skip(1).Where(l => l.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)).Select(l => l[13..].Trim()).FirstOrDefault() ?? "(none)";
        bool chunked = lines.Skip(1).Any(l => l.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase) && l.Contains("chunked"));
        var bodyBytes = new List<byte>();
        async Task<bool> More()
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) return false;
            pending.AddRange(buffer.AsSpan(0, n).ToArray());
            return true;
        }
        if (chunked)
        {
            while (true)
            {
                int eol;
                while ((eol = Find(pending, "\r\n")) < 0) if (!await More()) return;
                int size = Convert.ToInt32(Encoding.ASCII.GetString(pending.GetRange(0, eol).ToArray()), 16);
                pending.RemoveRange(0, eol + 2);
                while (pending.Count < size + 2) if (!await More()) return;
                bodyBytes.AddRange(pending.GetRange(0, size));
                pending.RemoveRange(0, size + 2);
                if (size == 0) break;
            }
        }
        else
        {
            while (pending.Count < length) if (!await More()) return;
            bodyBytes.AddRange(pending.GetRange(0, length));
            pending.RemoveRange(0, length);
        }
        string body = Encoding.UTF8.GetString(bodyBytes.ToArray());

        byte[] payload;
        string type = "application/json; charset=utf-8", status = "200 OK";
        switch (path)
        {
            case "/product": payload = Encoding.UTF8.GetBytes("{\"id\":7,\"name\":\"Widget \\u00e9\",\"price\":9.5,\"tags\":[\"a\",\"b\"],\"stock\":null}"); break;
            case "/products": payload = Encoding.UTF8.GetBytes("[{\"id\":1,\"name\":\"one\",\"price\":1,\"tags\":[]},{\"id\":2,\"name\":\"two\",\"price\":2.25,\"tags\":[\"x\"]}]"); break;
            case "/echo": payload = Encoding.UTF8.GetBytes("{\"contentType\":\"" + contentType + "\",\"received\":" + body + "}"); break;
            case "/utf16": payload = Encoding.Unicode.GetBytes("{\"id\":9,\"name\":\"wide \u00e9\u4e16\",\"price\":3,\"tags\":[]}"); type = "application/json; charset=utf-16"; break;
            case "/text": payload = Encoding.UTF8.GetBytes("{\"id\":1}"); type = "text/plain"; break;
            case "/broken": payload = Encoding.UTF8.GetBytes("{\"id\":"); break;
            case "/forbidden": payload = Encoding.UTF8.GetBytes("{}"); status = "403 Forbidden"; break;
            default: payload = Encoding.UTF8.GetBytes("null"); break;
        }
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + payload.Length + "\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(payload);
    }
}

static int Find(List<byte> data, string marker)
{
    var m = Encoding.ASCII.GetBytes(marker);
    for (int i = 0; i + m.Length <= data.Count; i++)
    {
        int j = 0;
        while (j < m.Length && data[i + j] == m[j]) j++;
        if (j == m.Length) return i;
    }
    return -1;
}

_ = Serve();
string url(string path) => "http://127.0.0.1:" + port + path;
using var http = new HttpClient();

var product = await http.GetFromJsonAsync(url("/product"), ShopJson.Default.Product);
Console.WriteLine(product);
Console.WriteLine("tags: " + string.Join("+", product!.Tags) + ", stock: " + (product.Stock?.ToString() ?? "none"));

var list = await http.GetFromJsonAsync(url("/products"), ShopJson.Default.ProductArray);
Console.WriteLine(list!.Length + " products: " + string.Join("; ", list.Select(p => p.Name + "=" + p.Price)));

// POST a value as JSON: the content type and body the server saw.
using (var response = await http.PostAsJsonAsync(url("/echo"), new Product(3, "Gadget", 19.99m, ["new"], 5), ShopJson.Default.Product))
{
    Console.WriteLine((int)response.StatusCode + " " + await response.Content.ReadAsStringAsync());
    Console.WriteLine("content type of the response: " + response.Content.Headers.ContentType);
}
using (var response = await http.PostAsync(url("/echo"), JsonContent.Create(new Product(4, "Thing", 1m, [], null), ShopJson.Default.Product)))
{
    var echo = await response.Content.ReadFromJsonAsync(ShopJson.Default.Echo);
    Console.WriteLine(echo!.ContentType + " / " + echo.Received);
}

// A UTF-16 body is transcoded.
using (var response = await http.GetAsync(url("/utf16")))
{
    var wide = await response.Content.ReadFromJsonAsync(ShopJson.Default.Product);
    Console.WriteLine(response.Content.Headers.ContentType + " -> " + wide);
}

// Failures.
try { await http.GetFromJsonAsync(url("/text"), ShopJson.Default.Product); }
catch (NotSupportedException e) { Console.WriteLine("text/plain: " + e.GetType().Name + ": " + e.Message); }
try { await http.GetFromJsonAsync(url("/broken"), ShopJson.Default.Product); }
catch (JsonException e) { Console.WriteLine("broken: " + e.GetType().Name + " at byte " + e.BytePositionInLine); }
try { await http.GetFromJsonAsync(url("/forbidden"), ShopJson.Default.Product); }
catch (HttpRequestException e) { Console.WriteLine("forbidden: " + e.StatusCode + " " + e.Message); }
Console.WriteLine("null body: " + (await http.GetFromJsonAsync(url("/nothing"), ShopJson.Default.Product) is null));

listener.Stop();

record Product(int Id, string Name, decimal Price, string[] Tags, int? Stock = null);

record Echo(string ContentType, Product Received);

[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(Product[]))]
[JsonSerializable(typeof(Echo))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
partial class ShopJson : JsonSerializerContext { }
