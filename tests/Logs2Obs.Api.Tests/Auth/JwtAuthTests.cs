namespace Logs2Obs.Api.Tests.Auth;

public class JwtAuthTests
{
    [Fact(Skip = "Requires JWT middleware integration.")]
    public void JwtAuth_WithValidToken_Succeeds()
    {
    }

    [Fact(Skip = "Requires JWT middleware integration.")]
    public void JwtAuth_WithExpiredToken_Returns401()
    {
    }

    [Fact(Skip = "Requires JWT middleware integration.")]
    public void JwtAuth_WithMissingToken_Returns401()
    {
    }

    [Fact(Skip = "Requires JWT middleware integration.")]
    public void JwtAuth_WithInvalidSignature_Returns401()
    {
    }
}
