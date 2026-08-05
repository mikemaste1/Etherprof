namespace Etherprof.Core;

using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;

public sealed class TestSetManager
{
    private readonly ITestRepository _repository;
    private List<TestSet> _testSets = new();

    public TestSetManager(ITestRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<TestSet> TestSets => _testSets;

    public async Task LoadAsync()
    {
        _testSets = await _repository.LoadAsync();
        _testSets.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
    }

    public async Task AddAsync(TestSet testSet)
    {
        testSet.SortOrder = _testSets.Count > 0 ? _testSets.Max(t => t.SortOrder) + 1 : 0;
        _testSets.Add(testSet);
        await _repository.SaveAsync(_testSets);
    }

    public async Task UpdateAsync(TestSet testSet)
    {
        var index = _testSets.FindIndex(t => t.Id == testSet.Id);
        if (index >= 0)
        {
            _testSets[index] = testSet;
            await _repository.SaveAsync(_testSets);
        }
    }

    public async Task DeleteAsync(Guid testSetId)
    {
        _testSets.RemoveAll(t => t.Id == testSetId);
        await _repository.SaveAsync(_testSets);
    }

    public async Task<TestSet> DuplicateAsync(Guid testSetId)
    {
        var source = _testSets.FirstOrDefault(t => t.Id == testSetId)
            ?? throw new InvalidOperationException($"Test set {testSetId} not found");

        var copy = new TestSet
        {
            Id = Guid.NewGuid(),
            Name = source.Name + " (copy)",
            Targets = source.Targets.Select(t => new TestTarget
            {
                Id = Guid.NewGuid(),
                Name = t.Name,
                Host = t.Host
            }).ToList(),
            SortOrder = _testSets.Count > 0 ? _testSets.Max(t => t.SortOrder) + 1 : 0
        };

        _testSets.Add(copy);
        await _repository.SaveAsync(_testSets);
        return copy;
    }
}
