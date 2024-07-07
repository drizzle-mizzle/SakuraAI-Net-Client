namespace SakuraAi.Exceptions;


/// <inheritdoc />
public class OperationFailedException : Exception
{
    /// <inheritdoc />
    public OperationFailedException(string message) : base(message) {}
}
