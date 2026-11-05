using OrderInventory.Application.Abstractions;

namespace OrderInventory.Infrastructure.Security;

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    public BcryptPasswordHasher()
    {
        DummyHash = BCrypt.Net.BCrypt.HashPassword("timing-equalization-placeholder");
    }

    public string DummyHash { get; }

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool Verify(string password, string passwordHash) =>
        BCrypt.Net.BCrypt.Verify(password, passwordHash);
}
