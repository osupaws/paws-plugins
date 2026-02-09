using System;
using System.Threading.Tasks;
using MyVuePlugin.Abstractions;
using MyVuePlugin.Models;
using Paws.Core.Abstractions;

namespace MyVuePlugin.Strategies.Stable
{
    public class StableStrategy : IMyStrategy
    {
        private readonly IHostServices _host;
        public string Name => "Stable Strategy";

        public StableStrategy(IHostServices host)
        {
            _host = host;
        }

        public async Task<object> ExecuteAsync(MyOptions options)
        {
            _host.LogMessage($"{Name} executing! Mode: {options.Mode}", PawsLogLvl.Information, "MyPlugin");

            string resultMessage = "";

            await _host.PerformStableWriteAsync(path =>
            {
                // Access 'osu!.db' here...
                resultMessage = $"Hello from osu!stable at {path}!";
            });

            return new
            {
                Success = true,
                Message = resultMessage
            };
        }
    }
}
