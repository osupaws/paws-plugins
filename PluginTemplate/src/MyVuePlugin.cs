using Paws.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace MyVuePlugin
{
    public class MyVuePlugin : IFunctionalExplicitPlugin
    {
        public Guid Id => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string Name => "My Vue Plugin";
        public string Description => "A template plugin using Vue 3 and Paws UI.";
        public string Version => "1.0.0";
        public string IconName => "palette";

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
                return new { Message = $"Vue sent: {payload}" };
            }
            return null;
        }
    }
}
