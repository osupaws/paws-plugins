using System.Threading.Tasks;
using MyPlugin.Models;

namespace MyPlugin.Abstractions
{
    public interface IMyStrategy
    {
        string Name { get; }
        Task<object> ExecuteAsync(MyOptions options);
    }
}
