namespace EndJinx.Connection;
using System.Net.Sockets;
using EndJinx.Parser;
using EndJinx.Router;
using EndJinx.Builder;
using EndJinx.Exceptions;

public class HttpConnection
{
    public async Task handleClient(TcpClient client)
    {
        using (client)
        {
            using (NetworkStream stream = client.GetStream())
            {
                try   
                {
                    var requestParser = new RequestParser();
                    var rawReq = await requestParser.ReadAllRequestsAsync(stream);
                    var req = requestParser.ParseReq(rawReq);
                    
                    var router = new Router();
                    var httpResp = router.HandleRequest(req);
                    var responseBuilder = new ResponseBuilder();
                    
                    var respBytes = responseBuilder.BuildHttpResponse(httpResp);
                    await stream.WriteAsync(respBytes.AsMemory());
                }
                catch (HttpException httpExc)
                {
                    var responseBuilder = new ResponseBuilder();
                    var respBytes = responseBuilder.BuildErrorResponse(httpExc.StatusCode, httpExc.StatusMessage, httpExc.Message);
                    await stream.WriteAsync(respBytes.AsMemory());
                }
                catch (Exception exc)
                {
                    Console.WriteLine($"Connection error: {exc}");
                    var responseBuilder = new ResponseBuilder();
                    var respBytes = responseBuilder.BuildErrorResponse(500, "Internal Server Error", "An unexpected error occurred");
                    await stream.WriteAsync(respBytes.AsMemory());
                }     
            }
        }
    }
}