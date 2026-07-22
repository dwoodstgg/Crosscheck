using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Clients;

public interface IClientRepository
{
    Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Client?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(Client client, CancellationToken cancellationToken = default);

    Task UpdateAsync(Client client, CancellationToken cancellationToken = default);

    Task SetActiveAsync(Guid clientId, bool isActive, CancellationToken cancellationToken = default);
}
