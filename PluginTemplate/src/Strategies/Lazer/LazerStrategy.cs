using System;
using System.Threading.Tasks;
using MyPlugin.Abstractions;
using MyPlugin.Models;
using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Interfaces.Services;

namespace MyPlugin.Strategies.Lazer
{
    public class LazerStrategy : IMyStrategy
    {
        private readonly IHost _host;
        public string Name => "Lazer Strategy";

        public LazerStrategy(IHost host)
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
