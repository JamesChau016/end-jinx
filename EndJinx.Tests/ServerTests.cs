using System.Text;
using EndJinx.Builder;
using EndJinx.Exceptions;
using EndJinx.Parser;
using EndJinx.Router;

namespace EndJinx.Tests;

public class FlagshipTests
{
    [Fact]
    public void ParseReq_ParsesRequestLineHeadersAndBody()
    {
        var rawRequest = "POST /echo HTTP/1.1\r\n" +
                         "Host: localhost\r\n" +
                         "Content-Length: 5\r\n\r\n" +
                         "hello";

        var request = new RequestParser().ParseReq(Encoding.UTF8.GetBytes(rawRequest));

        Assert.Equal("POST", request.Method);
        Assert.Equal("/echo", request.Path);
        Assert.Equal("HTTP/1.1", request.Version);
        Assert.Equal("localhost", request.Headers["host"]);
        Assert.Equal("hello", Encoding.UTF8.GetString(request.Body));
    }

    [Fact]
    public void ParseReq_RejectsBodyWhoseLengthDoesNotMatch()
    {
        var rawRequest = "POST /echo HTTP/1.1\r\n" +
                         "Content-Length: 6\r\n\r\n" +
                         "hello";

        Assert.Throws<BadRequestException>(() =>
            new RequestParser().ParseReq(Encoding.UTF8.GetBytes(rawRequest)));
    }

    [Fact]
    public void Router_ReturnsHelloWorldForRootRoute()
    {
        var request = new Request(
            "GET",
            "/",
            "HTTP/1.1",
            new Dictionary<string, string>(),
            Array.Empty<byte>());

        var response = new global::EndJinx.Router.Router().HandleRequest(request);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("Hello, World!", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public void Router_EchoesPostBody()
    {
        var body = Encoding.UTF8.GetBytes("hello");
        var request = new Request(
            "POST",
            "/echo",
            "HTTP/1.1",
            new Dictionary<string, string> { ["Content-Length"] = body.Length.ToString() },
            body);

        var response = new global::EndJinx.Router.Router().HandleRequest(request);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("hello", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public void Router_ThrowsNotFoundForUnknownRoute()
    {
        var request = new Request(
            "GET",
            "/missing",
            "HTTP/1.1",
            new Dictionary<string, string>(),
            Array.Empty<byte>());

        Assert.Throws<NotFoundException>(() => new global::EndJinx.Router.Router().HandleRequest(request));
    }

    [Fact]
    public void ResponseBuilder_IncludesBodyLengthAndConnectionPolicy()
    {
        var response = new HttpResponse(
            200,
            "OK",
            new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
            Encoding.UTF8.GetBytes("hello"));

        var bytes = new ResponseBuilder().BuildHttpResponse(response, keepAlive: true);
        var serialized = Encoding.UTF8.GetString(bytes);

        Assert.Contains("HTTP/1.1 200 OK\r\n", serialized);
        Assert.Contains("Content-Length: 5\r\n", serialized);
        Assert.Contains("Connection: keep-alive\r\n", serialized);
        Assert.EndsWith("\r\n\r\nhello", serialized);
    }
}
