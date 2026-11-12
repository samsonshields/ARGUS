namespace Argus.Core.Exceptions;

public sealed class SourceException(int statusCode, string message, Guid? existingSourceId = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public Guid? ExistingSourceId { get; } = existingSourceId;
}
