using OrderInventory.Domain.Entities;

namespace OrderInventory.Application.Abstractions;

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public sealed record RefreshTokenResult(string PlainText, string Hash, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(User user);

    RefreshTokenResult CreateRefreshToken();

    string HashRefreshToken(string plainText);
}
