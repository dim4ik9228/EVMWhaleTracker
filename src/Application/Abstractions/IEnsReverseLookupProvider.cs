using Domain.ValueObjects;

namespace Application.Abstractions;

public interface IEnsReverseLookupProvider
{
    Task<string?> ResolveNameAsync(Address address, CancellationToken cancellationToken);
}
