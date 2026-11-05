using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderInventory.Application.Abstractions;
using OrderInventory.Application.Common;
using OrderInventory.Application.Dtos;
using OrderInventory.Domain.Entities;
using OrderInventory.Domain.Enums;
using OrderInventory.Domain.Exceptions;

namespace OrderInventory.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwords;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IApplicationDbContext db,
        IPasswordHasher passwords,
        ITokenService tokens,
        IClock clock,
        ILogger<AuthService> logger)
    {
        _db = db;
        _passwords = passwords;
        _tokens = tokens;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var fullName = (request.FullName ?? string.Empty).Trim();
        var email = NormalizeEmail(request.Email);
        var password = request.Password ?? string.Empty;

        if (fullName.Length is < 2 or > 200)
            throw new ValidationException("Full name must be between 2 and 200 characters.");

        if (!IsValidEmail(email))
            throw new ValidationException("Email is not valid.");

        if (password.Length < 8 || password.Length > 100)
            throw new ValidationException("Password must be between 8 and 100 characters.");

        var emailTaken = await _db.Users.AnyAsync(u => u.Email == email, cancellationToken);
        if (emailTaken)
            throw new ConflictException("An account with this email already exists.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Email = email,
            PasswordHash = _passwords.Hash(password),
            Role = UserRole.Customer
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        _logger.LogInformation("Registered customer {UserId}", user.Id);
        return await IssueAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var password = request.Password ?? string.Empty;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        var hash = user?.PasswordHash ?? _passwords.DummyHash;
        var valid = _passwords.Verify(password, hash);
        if (user is null || !valid)
            throw new UnauthorizedAppException("Invalid email or password.");

        _logger.LogInformation("User {UserId} logged in", user.Id);
        return await IssueAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var plain = (request.RefreshToken ?? string.Empty).Trim();
        if (plain.Length == 0)
            throw new UnauthorizedAppException("Refresh token is required.");

        var hash = _tokens.HashRefreshToken(plain);
        var now = _clock.UtcNow;
        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= now)
            throw new UnauthorizedAppException("Refresh token is invalid or expired.");

        stored.RevokedAt = now;
        var response = await IssueAsync(stored.User, cancellationToken);
        _logger.LogInformation("Rotated refresh token for user {UserId}", stored.UserId);
        return response;
    }

    private async Task<AuthResponse> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var access = _tokens.CreateAccessToken(user);
        var refresh = _tokens.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refresh.Hash,
            ExpiresAt = refresh.ExpiresAtUtc,
            CreatedAt = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            access.Token,
            refresh.PlainText,
            access.ExpiresAtUtc,
            new UserResponse(user.Id, user.FullName, user.Email, user.Role.ToString()));
    }

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email)
    {
        if (email.Length is < 3 or > 256 || !email.Contains('@'))
            return false;

        try
        {
            var parsed = new MailAddress(email);
            return parsed.Address.Equals(email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

}
