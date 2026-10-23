namespace PokePic.Core;

/// <summary>
/// A problem found while parsing or validating a template or a data pack.
/// </summary>
/// <param name="Path">JSON path of the offending node, e.g. "elements[3].fill". Empty for the document root.</param>
/// <param name="Line">1-based line in the source, when known.</param>
/// <param name="Column">1-based column in the source, when known.</param>
public sealed record TemplateIssue(string Path, string Message, int? Line = null, int? Column = null)
{
    public override string ToString()
    {
        var location = Line is null ? Path : $"{Path} (line {Line})";
        return string.IsNullOrEmpty(location) ? Message : $"{location}: {Message}";
    }
}

/// <summary>
/// Thrown when a template cannot be prepared or rendered.
/// </summary>
public sealed class TemplateException : Exception
{
    public TemplateException(IReadOnlyList<TemplateIssue> issues)
        : base(string.Join(Environment.NewLine, issues))
    {
        Issues = issues;
    }

    public TemplateException(string path, string message, Exception? inner = null)
        : base(new TemplateIssue(path, message).ToString(), inner)
    {
        Issues = [new TemplateIssue(path, message)];
    }

    public IReadOnlyList<TemplateIssue> Issues { get; }
}
