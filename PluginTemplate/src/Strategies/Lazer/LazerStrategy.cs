using System;
using System.Threading.Tasks;
using MyVuePlugin.Abstractions;
using MyVuePlugin.Models;
using Paws.Core.Abstractions;

namespace MyVuePlugin.Strategies.Lazer
{
    public class LazerStrategy : IMyStrategy
    {
        private readonly IHostServices _host;
        public string Name => "Lazer Strategy";

        public LazerStrategy(IHostServices host)
        {
            _host = host;
        }

        public async Task<object> ExecuteAsync(MyOptions options)
        {
            _host.LogMessage($"{Name} executing! Mode: {options.Mode}, DryRun: {options.DryRun}", PawsLogLvl.Information, "MyPlugin");

            return await Task.FromResult(new
            {
                Success = true,
                Message = "Hello from osu!lazer strategy!"
            });
        }
    }
}
