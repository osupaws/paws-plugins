using System;
using System.Text.Json;
using System.Threading.Tasks;
using MyVuePlugin.Abstractions;
using MyVuePlugin.Models;
using MyVuePlugin.Strategies.Lazer;
using MyVuePlugin.Strategies.Stable;
using Paws.Core.Abstractions;

namespace MyPlugin
{
    public class MyPlugin : IFunctionalExplicitPlugin
    {
        public Guid Id => Guid.Parse("11111111-1111-1111-1111-111111111111");

        public string Name => "My Plugin";
        public string Description => "A template plugin using the Strategy Pattern.";
        public string Version => "1.0.0";
        public string IconName => "palette";

        private IHostServices? _host;

        public async Task Initialize(IHostServices hostServices)
        {
            _host = hostServices;
            // Initialization logic (e.g. logging)
            _host.LogMessage($"{Name} initialized correctly!", PawsLogLvl.Information, "MyPlugin");

            await Task.CompletedTask;
        }

        public async Task<object?> ExecuteCommandAsync(string commandName, object? payload)
        {
            if (_host == null) return null;

            try
            {
                // 1. Deserialize payload (assuming it's formatted as options)
                MyOptions data = new MyOptions();
                if (payload != null)
                {
                    var elements = (JsonElement)payload;
                    data = JsonSerializer.Deserialize<MyOptions>(elements.GetRawText()) ?? new MyOptions();
                }

                // 2. Determine Strategy (Router Pattern)
                bool isLegacy = false;
                try { isLegacy = ((dynamic)_host).IsLegacyMode; } catch { }

                IMyStrategy strategy = isLegacy
                    ? new StableStrategy(_host)
                    : new LazerStrategy(_host);

                // 3. Delegate execution
                return await strategy.ExecuteAsync(data);
            }
            catch (Exception ex)
            {
                _host.LogMessage($"{Name} Error: {ex.Message}", PawsLogLvl.Error, "MyPlugin");
                return new { Success = false, Error = ex.Message };
            }
        }
    }
}
