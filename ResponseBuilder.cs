namespace EndJinx.Builder;
using System.Text;

public record HttpResponse(
    int StatusCode,
    string StatusMessage,
    Dictionary<string, string> Headers,
    byte[] Body
);

public class ResponseBuilder
{

    public byte[] BuildHttpResponse(HttpResponse response)
    {
        var statusLine = $"HTTP/1.1 {response.StatusCode} {response.StatusMessage}\r\n";
        
        // Set Content-Length automatically
        var allHeaders = new Dictionary<string, string>(response.Headers)
        {
            { "Content-Length", response.Body.Length.ToString() },
            { "Connection", "close"}
        }; 
        
        var headerLines = string.Join("\r\n", allHeaders.Select(h => $"{h.Key}: {h.Value}"));
        var headerText = $"{statusLine}{headerLines}\r\n\r\n";
        
        var headerBytes = Encoding.UTF8.GetBytes(headerText);
        var responseBytes = headerBytes.Concat(response.Body).ToArray();
        
        Console.WriteLine(headerText.Trim());
        return responseBytes;
    }
    public byte[] BuildErrorResponse(int statusCode, string statusMessage, string message)
    {
        var errorResponse = new HttpResponse(
            StatusCode: statusCode,
            StatusMessage: statusMessage,
            Headers: new() { { "Content-Type", "text/plain" } },
            Body: Encoding.UTF8.GetBytes(message)
        );
        return BuildHttpResponse(errorResponse);
    }
}