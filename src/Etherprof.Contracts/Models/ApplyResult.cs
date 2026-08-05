namespace Etherprof.Contracts.Models;

public sealed class ApplyResult
{
    public bool Success { get; init; }
    public bool VerificationPassed { get; init; }
    public string? ErrorMessage { get; init; }

    public static ApplyResult Succeeded() => new() { Success = true, VerificationPassed = true };
    public static ApplyResult Failed(string error) => new() { Success = false, VerificationPassed = false, ErrorMessage = error };
    public static ApplyResult VerificationFailed(string error) => new() { Success = true, VerificationPassed = false, ErrorMessage = error };
}
