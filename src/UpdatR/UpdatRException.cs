namespace UpdatR;

/// <summary>
/// Base type for exceptions representing a user-facing domain error, e.g. a target that can't be
/// updated or a malformed solution file. Unlike a plain <see cref="ArgumentException"/> - which
/// UpdatR still uses for classic "you passed a null/empty argument" programmer mistakes - this is
/// meant for problems caused by the content UpdatR was asked to operate on, so a consumer such as
/// <c>dotnet-updatr</c>'s CLI can catch a single type and render a friendly error message instead
/// of an unhandled stack trace.
/// </summary>
public class UpdatRException : Exception
{
    public UpdatRException(string message)
        : base(message) { }

    public UpdatRException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when the <c>path</c> supplied to <see cref="Updater.UpdateAsync"/> (or resolved from a
/// <c>.updatrrc</c>'s <c>path</c> property) doesn't point to anything UpdatR can update - it
/// doesn't exist, isn't a supported file type, a directory/solution contains nothing to update,
/// or a solution file couldn't be parsed.
/// </summary>
public sealed class InvalidUpdateTargetException : UpdatRException
{
    public InvalidUpdateTargetException(string message)
        : base(message) { }

    public InvalidUpdateTargetException(string message, Exception innerException)
        : base(message, innerException) { }
}
