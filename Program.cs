using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EndJinx.Exceptions;
using Microsoft.VisualBasic;

int MAX_HEADER_SIZE = 8*1024;

var server = new TcpListener(IPAddress.Loopback, 8000);
server.Start();
Console.WriteLine("Connection started");

while (true) {
    var client = await server.AcceptTcpClientAsync();
    _ = Task.Run(() => handleClient(client));
}

async Task handleClient(TcpClient client)
{
    using (client)
    {
        using (NetworkStream stream = client.GetStream())
        {
            try   
            {
                var rawReq = await readAllRequestsAsync(stream);

                var req = parseReq(rawReq);
                var resp = processMsg(req);
                await stream.WriteAsync(resp.AsMemory());
            }
            catch (BadRequestException badReqExc)
            {
                var len = badReqExc.Message.Length;
                var resp = Encoding.UTF8.GetBytes($"HTTP/1.1 400 Bad Request\r\nContent-Length: {len}\r\nContent-Type:text/plain\r\n\r\n{badReqExc.Message}");
                await stream.WriteAsync(resp.AsMemory());
            }
            catch (Exception exc)
            {
                Console.WriteLine($"Connection error: {exc}");
            }     
        }
    }
}

Byte[] processMsg(Request request)
{
    var type = request.Headers.TryGetValue("content-type", out var contentType)
        ? contentType
        : "text/plain";
    
    var body = Encoding.UTF8.GetString(request.Body);
    var bodyBytes = Encoding.UTF8.GetBytes(body);
    
    var statCode = "200";

    var res = $"{request.Version} {statCode} OK\r\nContent-Length: {bodyBytes.Length}\r\nContent-Type: {type}; charset=utf-8\r\n\r\n";
    var resBytes = Encoding.UTF8.GetBytes(res).Concat(bodyBytes).ToArray();
    Console.WriteLine(res);
    return resBytes;
}

async Task<byte[]> readAllRequestsAsync(NetworkStream stream)
{
    var processedBytes = new List<byte>();
    int contentLength = 0;
    string? contentType = null;

    byte[] crlfCrlf = new byte[] { 13, 10, 13, 10 }; // \r\n\r\n

    while (true)
    {
        var buffer = new byte[1024];
        int bytes = await stream.ReadAsync(buffer.AsMemory());

        if (bytes == 0)
        {
            break;
        }

        for (int i = 0; i < bytes; i++)
        {
            processedBytes.Add(buffer[i]);
        }

        if (processedBytes.Count > MAX_HEADER_SIZE)
        {
            Console.WriteLine("Error: request header too large");
            throw new Exception("Error: request header too large");
        }

        // look for end of headers in raw bytes
        int headerEnd = IndexOfSequence(processedBytes, crlfCrlf);
        if (headerEnd >= 0)
        {
            // parse headers from bytes up to headerEnd
            var headerBytes = processedBytes.GetRange(0, headerEnd).ToArray();
            var headersText = Encoding.UTF8.GetString(headerBytes);

            foreach (var line in headersText.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line.Substring("Content-Length:".Length).Trim();
                    int.TryParse(value, out contentLength);
                }

                if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = line.Substring("Content-Type:".Length).Trim();
                }
            }

            // compute how many body bytes we already have after header terminator
            int bodyAlready = processedBytes.Count - (headerEnd + 4);
            int remaining = contentLength - bodyAlready;

            // read remaining body bytes, if any
            while (remaining > 0)
            {
                int toRead = Math.Min(1024, remaining);
                var tmp = new byte[toRead];
                int n = await stream.ReadAsync(tmp.AsMemory());
                if (n == 0) break; // premature EOF
                for (int i = 0; i < n; i++) processedBytes.Add(tmp[i]);
                remaining -= n;
            }

            break; // we have headers and (if any) the full body
        }
    }

    return processedBytes.ToArray();
}

Request parseReq(byte[] bytes){
    var request = Encoding.UTF8.GetString(bytes);

    if (string.IsNullOrWhiteSpace(request))
    {
        throw new BadRequestException("Error: Empty request");
    }

    var requestParts = request.Split("\r\n\r\n", 2, StringSplitOptions.None);
    var body = requestParts.Length > 1 ? requestParts[1] : string.Empty;

    var lines = request.Split("\r\n", StringSplitOptions.None);
    var headers = new Dictionary<String,String>();

    var reqLineIdx = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
    if (reqLineIdx<0)
    {
        throw new BadRequestException("Error: Empty request");
    }

    var parts = lines[reqLineIdx].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 3)
    {
        throw new BadRequestException("Error: Malformed request");
    }
    var method = parts[0];
    var path = parts[1];
    var version = parts[2];

    foreach (var line in lines.Skip(reqLineIdx+1)){
        if (string.IsNullOrWhiteSpace(line)) break; // reached blank line before body
        int idx = line.IndexOf(":");
        if (idx<1) throw new BadRequestException($"Malformed header: {line}");
        var key = line[..idx].Trim();
        var val = line[(idx+1)..].Trim();
        headers[key] = val;
    }

    return new Request(
        Method: method,
        Path: path,
        Version: version,
        Headers: headers,
        Body: Encoding.UTF8.GetBytes(body)
    );
}

static int IndexOfSequence(List<byte> haystack, byte[] needle)
{
    if (needle.Length == 0) return 0;
    for (int i = 0; i <= haystack.Count - needle.Length; i++)
    {
        bool match = true;
        for (int j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j]) { match = false; break; }
        }
        if (match) return i;
    }
    return -1;
}
record Request(string Method, string Path, string Version, Dictionary<string, string> Headers, byte[] Body);
