namespace EndJinx.Router;
using EndJinx.Exceptions;
using EndJinx.Builder;
using EndJinx.Parser;
using System.Text;

public class Router
{
    public HttpResponse HandleRequest(Request request)
    {
        return (request.Method, request.Path) switch
        {
            ("GET", "/") => new HttpResponse(
                StatusCode: 200,
                StatusMessage: "OK",
                Headers: new() { { "Content-Type", "text/plain" } },
                Body: Encoding.UTF8.GetBytes("Hello, World!")
            ),
            ("POST", "/echo") => HandleEcho(request),
            (_, _) => throw new NotFoundException($"Route not found: {request.Method} {request.Path}")
        };
    }

    private HttpResponse HandleEcho(Request request)
    {
        var body = Encoding.UTF8.GetString(request.Body);
        var responseBody = Encoding.UTF8.GetBytes(body);
        
        return new HttpResponse(
            StatusCode: 200,
            StatusMessage: "OK",
            Headers: new() { { "Content-Type", "text/plain" } },
            Body: responseBody
        );
    }
}