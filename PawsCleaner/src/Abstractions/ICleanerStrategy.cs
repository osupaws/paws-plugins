using PawsCleaner.Models;

namespace PawsCleaner.Abstractions
{
    public interface ICleanerStrategy
    {
        Task<object> CleanAsync(CleanerOptions options);
        string Name { get; }
    }
}
