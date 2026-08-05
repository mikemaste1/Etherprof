namespace Etherprof.Core;

using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;

public sealed class ProfileManager
{
    private readonly IProfileRepository _repository;
    private List<NetworkProfile> _profiles = new();

    public ProfileManager(IProfileRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<NetworkProfile> Profiles => _profiles;

    public async Task LoadAsync()
    {
        _profiles = await _repository.LoadAsync();
        _profiles.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
    }

    public async Task AddAsync(NetworkProfile profile)
    {
        profile.SortOrder = _profiles.Count > 0 ? _profiles.Max(p => p.SortOrder) + 1 : 0;
        _profiles.Add(profile);
        await _repository.SaveAsync(_profiles);
    }

    public async Task UpdateAsync(NetworkProfile profile)
    {
        var index = _profiles.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
        {
            _profiles[index] = profile;
            await _repository.SaveAsync(_profiles);
        }
    }

    public async Task DeleteAsync(Guid profileId)
    {
        _profiles.RemoveAll(p => p.Id == profileId);
        await _repository.SaveAsync(_profiles);
    }

    public async Task<NetworkProfile> DuplicateAsync(Guid profileId)
    {
        var source = _profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new InvalidOperationException($"Profile {profileId} not found");

        var copy = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = source.Name + " (copy)",
            Type = source.Type,
            IPv4 = source.IPv4 is not null ? new IPv4Configuration
            {
                Address = source.IPv4.Address,
                PrefixLength = source.IPv4.PrefixLength,
                Gateway = source.IPv4.Gateway
            } : null,
            ApplyDns = source.ApplyDns,
            Dns = source.Dns is not null ? new DnsConfiguration
            {
                Mode = source.Dns.Mode,
                Servers = new List<string>(source.Dns.Servers)
            } : null,
            SortOrder = _profiles.Count > 0 ? _profiles.Max(p => p.SortOrder) + 1 : 0
        };

        _profiles.Add(copy);
        await _repository.SaveAsync(_profiles);
        return copy;
    }
}
