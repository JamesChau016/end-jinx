namespace EndJinx.Connection;
using System.Net.Sockets;
using EndJinx.Parser;
using EndJinx.Router;
using EndJinx.Builder;
using EndJinx.Exceptions;
using EndJinx.Logging;

public class HttpConnection
{
    private readonly ILogger logger;

    public HttpConnection(ILogger? logger = null)
    {
        this.logger = logger ?? new ConsoleLogger();
    }

    public async Task handleClient(TcpClient client)
    {
        using var tempClient = client;
        using NetworkStream stream = client.GetStream();
        
        try   
        {
            var parser = new RequestParser();
            var router = new Router();
            var builder = new ResponseBuilder(logger);
            while (true)
            {
                var rawReq = await parser.ReadAllRequestsAsync(stream);

                if (rawReq.Length == 0)
                {
                    break;
                }

                var req = parser.ParseReq(rawReq);
                var httpResp = router.HandleRequest(req);

                var keepAlive = KeepAlive(req);
                var respBytes = builder.BuildHttpResponse(httpResp, keepAlive);

                await stream.WriteAsync(respBytes.AsMemory());

                if (!keepAlive)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.Info("Client timed out.");
        }
        catch (HttpException httpExc)
        {
            var responseBuilder = new ResponseBuilder();
            var respBytes = responseBuilder.BuildErrorResponse(httpExc.StatusCode, httpExc.StatusMessage, httpExc.Message);
            await stream.WriteAsync(respBytes.AsMemory());
        }
        catch (Exception exc)
        {
            logger.Error("Connection error", exc);
            var responseBuilder = new ResponseBuilder();
            var respBytes = responseBuilder.BuildErrorResponse(500, "Internal Server Error", "An unexpected error occurred");
            await stream.WriteAsync(respBytes.AsMemory());
        }     

    }

    private static bool KeepAlive(Request request)
    {
        if (!request.Headers.TryGetValue("Connection", out var connectionHeader))
        {
            return request.Version.Equals("HTTP/1.1", StringComparison.OrdinalIgnoreCase);
        }

        return !connectionHeader.Equals("close", StringComparison.OrdinalIgnoreCase);
    }
}