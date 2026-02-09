using System.Threading.Tasks;
using MyVuePlugin.Models;

namespace MyVuePlugin.Abstractions
{
    public interface IMyStrategy
    {
        string Name { get; }
        Task<object> ExecuteAsync(MyOptions options);
    }
}
