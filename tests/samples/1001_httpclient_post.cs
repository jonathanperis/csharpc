// HttpClient requests with a body over plain HTTP/1.1 against a loopback echo server in the same program: POST with
// StringContent, ByteArrayContent, FormUrlEncodedContent, MultipartFormDataContent and a StreamContent of unknown
// length (chunked), PUT / PATCH / DELETE, content and authorization headers, and what the server received.
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

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

// Reads requests (Content-Length or chunked bodies) and answers each with a description of what it received.
async Task Handle(TcpClient client)
{
    using var tcp = client;
    var stream = tcp.GetStream();
    var buffer = new byte[8192];
    var pending = new List<byte>();
    async Task<bool> Fill()
    {
        int n = await stream.ReadAsync(buffer);
        if (n == 0) return false;
        pending.AddRange(buffer.AsSpan(0, n).ToArray());
        return true;
    }
    while (true)
    {
        int end;
        while ((end = IndexOf(pending, "\r\n\r\n")) < 0)
            if (!await Fill()) return;
        string head = Encoding.ASCII.GetString(pending.GetRange(0, end).ToArray());
        pending.RemoveRange(0, end + 4);
        var lines = head.Split("\r\n");
        var headers = lines.Skip(1).Select(l => (Name: l[..l.IndexOf(':')], Value: l[(l.IndexOf(':') + 1)..].Trim())).ToList();
        string? Header(string name) => headers.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        var body = new List<byte>();
        if (Header("Transfer-Encoding") == "chunked")
        {
            while (true)
            {
                int eol;
                while ((eol = IndexOf(pending, "\r\n")) < 0) if (!await Fill()) return;
                int size = Convert.ToInt32(Encoding.ASCII.GetString(pending.GetRange(0, eol).ToArray()), 16);
                pending.RemoveRange(0, eol + 2);
                while (pending.Count < size + 2) if (!await Fill()) return;
                body.AddRange(pending.GetRange(0, size));
                pending.RemoveRange(0, size + 2);
                if (size == 0) break;
            }
        }
        else if (Header("Content-Length") is { } length)
        {
            int count = int.Parse(length);
            while (pending.Count < count) if (!await Fill()) return;
            body.AddRange(pending.GetRange(0, count));
            pending.RemoveRange(0, count);
        }
        var text = new StringBuilder();
        text.Append(lines[0].Replace(port.ToString(), "PORT")).Append('\n');
        foreach (var h in headers)
            if (h.Name is not ("Host" or "Content-Type" or "Content-Length" or "Transfer-Encoding" or "Authorization" or "X-Token" or "Content-Language" or "Content-Encoding" or "Content-Disposition"))
                continue;
            else text.Append("  ").Append(h.Name).Append(": ").Append(h.Value.Replace(port.ToString(), "PORT")).Append('\n');
        text.Append("  body (").Append(body.Count).Append(" bytes): ").Append(Printable(body));
        byte[] payload = Encoding.UTF8.GetBytes(text.ToString());
        var head2 = Encoding.ASCII.GetBytes("HTTP/1.1 201 Created\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: " + payload.Length + "\r\n\r\n");
        await stream.WriteAsync(head2);
        await stream.WriteAsync(payload);
    }
}

// UTF-8 text with control characters and invalid bytes escaped.
static string Printable(List<byte> body)
{
    var sb = new StringBuilder();
    foreach (char c in Encoding.UTF8.GetString(body.ToArray()))
        sb.Append(c switch { '\r' => "\\r", '\n' => "\\n", < ' ' or '�' => "\\u" + ((int)c).ToString("x4"), _ => c.ToString() });
    return sb.ToString();
}

static int IndexOf(List<byte> data, string marker)
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

async Task Show(string label, Task<HttpResponseMessage> call)
{
    using var response = await call;
    Console.WriteLine("== " + label + ": " + (int)response.StatusCode + " " + response.ReasonPhrase);
    Console.WriteLine(await response.Content.ReadAsStringAsync());
}

await Show("StringContent", http.PostAsync(url("/string"), new StringContent("hello, wörld", Encoding.UTF8, "text/plain")));
await Show("StringContent (default type)", http.PostAsync(url("/default"), new StringContent("{\"a\":1}")));
await Show("StringContent (media type)", http.PostAsync(url("/json"), new StringContent("{\"a\":1}", Encoding.UTF8, "application/json")));
await Show("ByteArrayContent", http.PostAsync(url("/bytes"), new ByteArrayContent([1, 2, 3, 255])));

var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "a b&c", ["x"] = "ü=1", ["empty"] = "" });
await Show("FormUrlEncodedContent", http.PostAsync(url("/form"), form));

var multipart = new MultipartFormDataContent("sample-boundary")
{
    { new StringContent("value one"), "first" },
    { new StringContent("file text"), "second", "note.txt" },
};
await Show("MultipartFormDataContent", http.PostAsync(url("/multipart"), multipart));

// A stream of unknown length is sent chunked.
var streamContent = new StreamContent(new MemoryStream(Encoding.ASCII.GetBytes("streamed body")));
await Show("StreamContent (known length)", http.PostAsync(url("/stream"), streamContent));
var unknown = new StreamContent(new ForwardOnlyStream(Encoding.ASCII.GetBytes("chunked body")));
await Show("StreamContent (unknown length)", http.PostAsync(url("/unknown"), unknown));

// Other methods, content headers and Authorization.
using (var put = new HttpRequestMessage(HttpMethod.Put, url("/put")) { Content = new StringContent("replace", Encoding.ASCII, "text/plain") })
{
    put.Content.Headers.ContentLanguage.Add("en");
    put.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("user:secret")));
    put.Headers.Add("X-Token", "abc123");
    await Show("PUT", http.SendAsync(put));
    Console.WriteLine("content length: " + put.Content.Headers.ContentLength + ", type " + put.Content.Headers.ContentType);
}
await Show("PATCH", http.PatchAsync(url("/patch"), new StringContent("part")));
await Show("DELETE", http.DeleteAsync(url("/delete")));
await Show("PostAsync(null)", http.PostAsync(url("/nothing"), null));
using (var custom = new HttpRequestMessage(new HttpMethod("PURGE"), url("/purge")))
    await Show("custom method", http.SendAsync(custom));

// A content object reads back what it holds.
var content = new StringContent("abc", Encoding.UTF8, "text/plain");
Console.WriteLine("content: " + await content.ReadAsStringAsync() + " / " + (await content.ReadAsByteArrayAsync()).Length + " bytes / " + content.Headers.ContentLength);
Console.WriteLine("form: " + await form.ReadAsStringAsync());

listener.Stop();

// A stream that can't report its length or seek.
sealed class ForwardOnlyStream(byte[] data) : Stream
{
    int position;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = Math.Min(count, Math.Min(5, data.Length - position));
        Array.Copy(data, position, buffer, offset, n);
        position += n;
        return n;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
