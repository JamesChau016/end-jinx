namespace EndJinx.Exceptions;

public class HttpException : Exception
{
    public int StatusCode { get; }
    public string StatusMessage { get; }

    public HttpException(int statusCode, string statusMessage, string message)
        : base(message)
    {
        StatusCode = statusCode;
        StatusMessage = statusMessage;
    }
}

public class BadRequestException : HttpException
{
    public BadRequestException(string message)
        : base(400, "Bad Request", message) { }
}

public class ContentTooLargeException : HttpException
{
    public ContentTooLargeException(string message)
        : base(413, "Content Too Large", message) { }
}

public class NotFoundException : HttpException
{
    public NotFoundException(string message)
        : base(404, "Not Found", message) { }
}

public class InternalServerErrorException : HttpException
{
    public InternalServerErrorException(string message)
        : base(500, "Internal Server Error", message) { }
}