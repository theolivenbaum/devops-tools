namespace Azdo.Core.AzureDevOps;

/// <summary>
/// Indicates that some (but not all) projects failed during a multi-project
/// fetch. The caller receives valid data from the successful projects alongside
/// this error.
/// </summary>
public sealed class PartialException : Exception
{
    /// <summary>Number of projects that failed.</summary>
    public int Failed { get; }
    /// <summary>Total number of projects.</summary>
    public int Total { get; }
    /// <summary>Individual project errors.</summary>
    public IReadOnlyList<Exception> Errors { get; }

    /// <summary>
    /// The successfully-fetched, merged-and-sorted partial results. Boxed as
    /// <see cref="object"/>; cast to the expected <c>List&lt;T&gt;</c> (e.g.
    /// <c>List&lt;PipelineRun&gt;</c>) to recover the partial data.
    /// </summary>
    public object? PartialData { get; init; }

    public PartialException(int failed, int total, IReadOnlyList<Exception> errors)
        : base($"{failed} of {total} projects failed to load")
    {
        Failed = failed;
        Total = total;
        Errors = errors;
    }
}

/// <summary>
/// An error raised when an Azure DevOps API request returns a non-2xx status.
/// Carries the classified, user-friendly message; the raw response body is
/// intentionally never included.
/// </summary>
public sealed class AzdoHttpException : Exception
{
    public int StatusCode { get; }

    public AzdoHttpException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
