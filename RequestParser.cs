namespace EndJinx.Parser;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EndJinx.Exceptions;

public record Request(string Method, string Path, string Version, Dictionary<string, string> Headers, byte[] Body);

public class RequestParser
{
    private const int MAX_HEADER_SIZE = 8 * 1024;

    public async Task<byte[]> ReadAllRequestsAsync(NetworkStream stream)
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