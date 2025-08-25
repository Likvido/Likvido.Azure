using System.Security.Claims;
using System.Security.Principal;
using Likvido.Azure.Extensions;
using Shouldly;
using Xunit;

namespace UnitTests.Extensions;

public class PrincipalExtensionsTests
{
    [Fact]
    public void GetAllClaims_WhenPrincipalIsNull_ReturnsEmptyList()
    {
        // Arrange
        IPrincipal principal = null!;

        // Act
        var claims = principal.GetAllClaims();

        // Assert
        claims.ShouldNotBeNull();
        claims.ShouldBeEmpty();
    }

    [Fact]
    public void GetAllClaims_WhenPrincipalIsNotClaimsPrincipal_ReturnsEmptyList()
    {
        // Arrange: create a custom IPrincipal that is NOT a ClaimsPrincipal
        var identity = new GenericIdentity("user");
        IPrincipal principal = new NonClaimsPrincipal(identity);

        // Act
        var claims = principal.GetAllClaims();

        // Assert
        claims.ShouldNotBeNull();
        claims.ShouldBeEmpty();
    }

    [Fact]
    public void GetAllClaims_WhenSingleIdentity_ReturnsAllClaims()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "123"),
            new Claim(ClaimTypes.Name, "Alice"),
            new Claim("custom", "value")
        }, "testAuth");
        IPrincipal principal = new ClaimsPrincipal(identity);

        // Act
        var claims = principal.GetAllClaims();

        // Assert
        claims.Count.ShouldBe(3);
        claims.ShouldContain(kv => kv.Key == ClaimTypes.NameIdentifier && kv.Value == "123");
        claims.ShouldContain(kv => kv.Key == ClaimTypes.Name && kv.Value == "Alice");
        claims.ShouldContain(kv => kv.Key == "custom" && kv.Value == "value");
    }

    [Fact]
    public void GetAllClaims_WhenMultipleIdentities_AggregatesAllClaims()
    {
        // Arrange
        var identity1 = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Email, "a@example.com"),
            new Claim("scope", "read")
        }, "auth1");

        var identity2 = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Email, "b@example.com"),
            new Claim("scope", "write")
        }, "auth2");

        IPrincipal principal = new ClaimsPrincipal(new[] { identity1, identity2 });

        // Act
        var claims = principal.GetAllClaims();

        // Assert
        claims.Count.ShouldBe(4);
        claims.ShouldContain(kv => kv.Key == ClaimTypes.Email && kv.Value == "a@example.com");
        claims.ShouldContain(kv => kv.Key == "scope" && kv.Value == "read");
        claims.ShouldContain(kv => kv.Key == ClaimTypes.Email && kv.Value == "b@example.com");
        claims.ShouldContain(kv => kv.Key == "scope" && kv.Value == "write");
    }

    [Fact]
    public void GetAllClaims_WhenDuplicateTypes_IncludesAllOccurrences()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("role", "admin"),
            new Claim("role", "editor")
        }, "auth");
        IPrincipal principal = new ClaimsPrincipal(identity);

        // Act
        var claims = principal.GetAllClaims();

        // Assert
        claims.Count.ShouldBe(2);
        claims.ShouldContain(kv => kv.Key == "role" && kv.Value == "admin");
        claims.ShouldContain(kv => kv.Key == "role" && kv.Value == "editor");
    }

    private sealed class NonClaimsPrincipal : IPrincipal
    {
        public IIdentity Identity { get; }
        public NonClaimsPrincipal(IIdentity identity) => Identity = identity;
        public bool IsInRole(string role) => false;
    }
}
