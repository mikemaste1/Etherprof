namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public interface ITestRepository
{
    Task<List<TestSet>> LoadAsync();
    Task SaveAsync(List<TestSet> testSets);
}
