using System.Collections;

namespace SakuraAi.Client.Exceptions;


/// <inheritdoc />
public class SakuraAiException : Exception
{
    /// <summary>
    /// HTTP status code
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// HTTP response
    /// </summary>
    public string Details { get; }


    /// <inheritdoc />
    public override string ToString()
        => $"[ Response ]\n{Details}\n[ StackTrace ]\n{base.ToString()}";


    /// <inheritdoc />
    public SakuraAiException(string message, int statusCode, string data) : base(message)
    {
        StatusCode = statusCode;
        Details = data;
    }
}
