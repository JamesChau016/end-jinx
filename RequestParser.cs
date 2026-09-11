namespace EndJinx.Parser;
using System.Net.Sockets;
using System.Text;
using EndJinx.Exceptions;

public record Request(string Method, string Path, string Version, Dictionary<string, string> Headers, byte[] Body);

public class RequestParser
{
    private const int MAX_HEADER_SIZE = 8 * 1024;
    private const int MAX_BODY_SIZE = 1024 * 1024;
    private readonly List<byte> pendingBytes = new();

    public async Task<byte[]> ReadAllRequestsAsync(NetworkStream stream)
    {
        int contentLength = 0;

        byte[] crlfCrlf = new byte[] { 13, 10, 13, 10 }; // \r\n\r\n

        while (true)
        {
            // look for end of headers in raw bytes
            int headerEnd = IndexOfSequence(pendingBytes, crlfCrlf);
            
            if (headerEnd > MAX_HEADER_SIZE
                || (headerEnd < 0 && pendingBytes.Count > MAX_HEADER_SIZE))
            { 
                throw new BadRequestException("Error: request header too large");
            }

            if (headerEnd >= 0)
            {
                // parse headers from bytes up to headerEnd
                var headerBytes = pendingBytes.GetRange(0, headerEnd).ToArray();
                var headersText = Encoding.UTF8.GetString(headerBytes);

                foreach (var line in headersText.Split("\r\n"))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Substring("Content-Length:".Length).Trim();
                        if (!int.TryParse(value, out contentLength) || contentLength < 0)
                        {
                            throw new BadRequestException("Error: invalid content length value");
                        }
                        if (contentLength > MAX_BODY_SIZE){
                            throw new ContentTooLargeException("Error: Body too large");
                        }
                    }
                }

                int requestEnd = headerEnd + 4 + contentLength;
                int remaining = requestEnd - pendingBytes.Count;

                // read remaining body bytes, if any
                while (remaining > 0)
                {
                    int toRead = Math.Min(1024, remaining);
                    var tmp = new byte[toRead];
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    int n = await stream.ReadAsync(tmp.AsMemory(), cts.Token);
                    if (n == 0)
                    {
                        throw new BadRequestException("Error: incomplete request body");
                    }
                    for (int i = 0; i < n; i++) pendingBytes.Add(tmp[i]);
                    remaining -= n;
                }

                var requestBytes = pendingBytes.Take(requestEnd).ToArray();
                pendingBytes.RemoveRange(0, requestEnd);
                return requestBytes;
            }

            using var headerCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var buffer = new byte[1024];
            int bytes = await stream.ReadAsync(buffer.AsMemory(), headerCts.Token);

            if (bytes == 0)
            {
                return Array.Empty<byte>();
            }

            for (int i = 0; i < bytes; i++)
            {
                pendingBytes.Add(buffer[i]);
            }
        }
    }

    public Request ParseReq(byte[] bytes)
    {
        var request = Encoding.UTF8.GetString(bytes);

        if (string.IsNullOrWhiteSpace(request))
        {
            throw new BadRequestException("Error: Empty request");
        }

        var requestParts = request.Split("\r\n\r\n", 2, StringSplitOptions.None);
        var body = requestParts.Length > 1 ? requestParts[1] : string.Empty;

        var lines = request.Split("\r\n", StringSplitOptions.None);
        var headers = new Dictionary<String,String>(StringComparer.OrdinalIgnoreCase);

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
            if (idx<1) throw new BadRequestException($"ErrorL Malformed header: {line}");
            var key = line[..idx].Trim();
            var val = line[(idx+1)..].Trim();
            headers[key] = val;
        }

        if (!headers.TryGetValue("Content-Length", out var contentLengthText))
        {
            headers["Content-Length"]="0";
            contentLengthText="0";
        }

        if (!int.TryParse(contentLengthText, out int contentLength) || contentLength < 0)
        {
            throw new BadRequestException("Error: invalid content length value");
        }

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        
        if (bodyBytes.Length < contentLength)
        {
            throw new BadRequestException("Error: Body length less than content length");
        }
        else if (bodyBytes.Length > contentLength)
        {
            throw new BadRequestException("Error: Body length greater than content length");
        }
        if (bodyBytes.Length > MAX_BODY_SIZE)
        {
            throw new ContentTooLargeException("Error: Body too large");
        }

        return new Request(
            Method: method,
            Path: path,
            Version: version,
            Headers: headers,
            Body: bodyBytes
        );
    }

    private static int IndexOfSequence(List<byte> haystack, byte[] needle)
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
}