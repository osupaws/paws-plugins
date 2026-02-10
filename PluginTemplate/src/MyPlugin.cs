using System;
using System.Text.Json;
using System.Threading.Tasks;
using MyPlugin.Abstractions;
using MyPlugin.Models;
using MyPlugin.Strategies.Lazer;
using MyPlugin.Strategies.Stable;
using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Interfaces;
using Paws.Core.Abstractions.Interfaces.Services;

namespace MyPlugin
{
    public class MyPlugin : IPawsPlugin
    {
        public string Id => "author.template-plugin";

        public string Name => "My Plugin";
        public string Description => "A template plugin using the Strategy Pattern.";
        public string Version => "1.0.1";
        public string IconName => "palette";

        private IHost? _host;

        public Task Initialize(IHost host)
        {
            _host = host;
            // Initialization logic (e.g. logging)
            _host.LogMessage($"{Name} initialized correctly!", PawsLogLvl.Information, "MyPlugin");

            return Task.CompletedTask;
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
