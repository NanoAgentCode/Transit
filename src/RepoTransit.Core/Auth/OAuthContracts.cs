namespace RepoTransit;

public sealed record TokenResult(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);
public sealed record DeviceChallenge(string UserCode, string VerificationUri, string DeviceCode, int Interval, DateTimeOffset ExpiresAt);
