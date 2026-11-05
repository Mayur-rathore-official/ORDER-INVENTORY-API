namespace OrderInventory.Application.Abstractions;

public interface IPasswordHasher
{
    string DummyHash { get; }

    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
