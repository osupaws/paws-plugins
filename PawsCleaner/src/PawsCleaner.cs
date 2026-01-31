using Paws.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace PawsCleaner
{
    public class PawsCleanerPlugin : IFunctionalExplicitPlugin
    {
        public Guid Id => Guid.Parse("d34db33f-c001-4c33-9999-c1ea4e700001");
        public string Name => "Paws Cleaner";
        public string Description => "Efficiently clean up unused osu! files.";
        public string Version => "0.1.0";
        public string IconName => "delete";

        private IHostServices? _host;

        public void Initialize(IHostServices hostServices)
        {
            _host = hostServices;
            _host.LogMessage($"{Name} initialized!");
        }

        public async Task<object?> ExecuteCommandAsync(string commandName, object? payload)
        {
            if (commandName == "greet")
            {
                return new { Message = $"Cleaner says: {payload}" };
            }
            return null;
        }
    }
}
