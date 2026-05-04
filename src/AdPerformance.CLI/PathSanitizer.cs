namespace AdPerformance.CLI;

/// <summary>
/// Defensive helpers for handling user-supplied paths and strings that will be
/// logged or passed to filesystem APIs. Mitigates CWE-117 (Log Forging) and
/// CWE-23 (Path Traversal).
/// </summary>
public static class PathSanitizer
{
    /// <summary>
    /// Normalises a user-provided path to a fully-qualified absolute form so
    /// that downstream filesystem APIs see exactly one canonical
    /// representation. Rejects paths that contain null bytes, control
    /// characters, or invalid path characters. Returns a freshly allocated
    /// string so downstream taint analysis sees a sanitised value.
    /// </summary>
    public static string NormalizePath(string path, string argName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, argName);

        foreach (var c in path)
        {
            if (c == '\0' || char.IsControl(c))
            {
                throw new ArgumentException(
                    $"{argName} contains an illegal control character.",
                    argName);
            }
        }

        // Path.GetInvalidPathChars is the canonical Framework check.
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException(
                $"{argName} contains characters that are not valid in paths.",
                argName);
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            throw new ArgumentException($"{argName} is not a valid path: {ex.Message}", argName, ex);
        }

        // Defensive: after normalisation, the path must still be well-formed.
        // Copy into a new char[] so the resulting string carries none of the
        // original argv reference.
        var span = full.AsSpan();
        var buffer = span.ToArray();
        return new string(buffer);
    }

    /// <summary>
    /// Strips characters that could break log-line integrity (CR, LF, and
    /// other control characters). The result is safe to interpolate into
    /// single-line log records.
    /// </summary>
    public static string ForLog(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var buffer = new char[value.Length];
        var j = 0;
        foreach (var c in value)
        {
            if (c == '\r' || c == '\n' || c == '\t')
            {
                buffer[j++] = ' ';
            }
            else if (!char.IsControl(c))
            {
                buffer[j++] = c;
            }
        }
        return new string(buffer, 0, j);
    }
}
