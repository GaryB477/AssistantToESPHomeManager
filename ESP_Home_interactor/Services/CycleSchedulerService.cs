using ESP_Home_Interactor.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ESP_Home_Interactor.Services;

/// <summary>
/// Background service that enforces the configured light cycle.
/// Every tick it determines the active phase and pushes the target
/// states to all actors whose actual state differs (this doubles as
/// retry when a command was lost or a device was offline).
/// A manual override suspends enforcement until the next phase change.
/// </summary>
public class CycleSchedulerService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Fan levels get re-sent unconditionally at this interval: the Controller 69
    /// firmware offers no reliable way to read per-port state, so external changes
    /// (app, display) can only be corrected by blind re-enforcement.
    /// </summary>
    private static readonly TimeSpan FanReEnforceInterval = TimeSpan.FromMinutes(5);

    private DateTime _lastFanEnforcement = DateTime.MinValue;

    private readonly ILogger<CycleSchedulerService> _logger;
    private readonly EspDeviceService _espService;
    private readonly AcInfinityService _acService;
    private readonly string _configPath;

    public CycleSchedulerService(ILogger<CycleSchedulerService> logger, EspDeviceService espService,
        AcInfinityService acService, IConfiguration configuration)
    {
        _logger = logger;
        _espService = espService;
        _acService = acService;
        _configPath = configuration["CycleConfigPath"] ??
                      "/Users/marc/git/private/HomePeter/ESP_Home_interactor/cycles.json";
    }

    public LightCycleConfig? CycleConfig { get; private set; }
    public CyclePhase? CurrentPhase { get; private set; }
    public bool OverrideActive { get; private set; }

    public event Action? OnCycleChanged;

    /// <summary>Suspend cycle enforcement until the next phase change</summary>
    public void ActivateOverride()
    {
        OverrideActive = true;
        _logger.LogInformation("Manual override activated (until next phase change)");
        OnCycleChanged?.Invoke();
    }

    /// <summary>Resume cycle enforcement and apply the current phase immediately</summary>
    public async Task DeactivateOverride()
    {
        OverrideActive = false;
        _logger.LogInformation("Manual override deactivated");
        if (CurrentPhase != null)
            await ApplyPhase(CurrentPhase);
        OnCycleChanged?.Invoke();
    }

    /// <summary>Persist an updated cycle configuration and apply it immediately</summary>
    public async Task UpdateConfig(LightCycleConfig config)
    {
        await config.Save(_configPath);
        CycleConfig = config;
        _logger.LogInformation("Cycle configuration updated");
        await Tick();
    }

    /// <summary>Start time of the next phase change, for display purposes</summary>
    public (string Name, TimeOnly Start)? NextPhase
    {
        get
        {
            if (CycleConfig == null || CycleConfig.Phases.Count == 0) return null;
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var sorted = CycleConfig.Phases.OrderBy(p => p.Start).ToList();
            var next = sorted.FirstOrDefault(p => p.Start > now) ?? sorted[0];
            return (next.Name, next.Start);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cycle Scheduler starting...");

        try
        {
            CycleConfig = await LightCycleConfig.Read(_configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read cycle configuration from {Path} - scheduler disabled", _configPath);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Tick();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cycle tick failed");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task Tick()
    {
        if (CycleConfig == null || CycleConfig.Phases.Count == 0) return;

        var activePhase = CycleConfig.GetActivePhase(TimeOnly.FromDateTime(DateTime.Now));

        if (CurrentPhase?.Name != activePhase.Name)
        {
            _logger.LogInformation("Phase change: {Old} -> {New}", CurrentPhase?.Name ?? "(none)", activePhase.Name);
            CurrentPhase = activePhase;
            OverrideActive = false;
            OnCycleChanged?.Invoke();
        }
        else
        {
            CurrentPhase = activePhase;
        }

        if (OverrideActive) return;

        await ApplyPhase(activePhase);
    }

    private async Task ApplyPhase(CyclePhase phase)
    {
        foreach (var actor in phase.Actors)
        {
            var device = _espService.Devices.FirstOrDefault(d => d.Host == actor.Host);
            if (device == null)
            {
                _logger.LogWarning("Actor {ObjectId}: device {Host} not configured", actor.ObjectId, actor.Host);
                continue;
            }

            var connection = device.Connection;
            if (connection == null)
            {
                _logger.LogWarning("Actor {ObjectId}: device {Host} is disconnected", actor.ObjectId, actor.Host);
                continue;
            }

            try
            {
                var light = device.LightEntities.FirstOrDefault(l => l.ObjectId == actor.ObjectId);
                if (light != null)
                {
                    var brightnessMismatch = actor.On && actor.Brightness.HasValue && light.SupportsBrightness &&
                                             Math.Abs(light.Brightness - actor.Brightness.Value) > 0.01f;
                    if (light.GetValue() != actor.On || brightnessMismatch || !light.HasState)
                    {
                        _logger.LogInformation("Phase '{Phase}': setting light {Name} to {State}",
                            phase.Name, light.Name, actor.On ? $"ON @ {actor.Brightness:P0}" : "OFF");
                        await light.SetStateAsync(connection, actor.On, actor.Brightness);
                    }
                    continue;
                }

                var switchEntity = device.SwitchEntities.FirstOrDefault(s => s.ObjectId == actor.ObjectId);
                if (switchEntity != null)
                {
                    if (switchEntity.GetValue() != actor.On || !switchEntity.HasState)
                    {
                        _logger.LogInformation("Phase '{Phase}': setting switch {Name} to {State}",
                            phase.Name, switchEntity.Name, actor.On ? "ON" : "OFF");
                        await switchEntity.SetStateAsync(connection, actor.On);
                    }
                    continue;
                }

                _logger.LogWarning("Actor {ObjectId} not found on device {Host}", actor.ObjectId, actor.Host);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to set actor {ObjectId} on {Host}: {Message}",
                    actor.ObjectId, actor.Host, ex.Message);
            }
        }

        var reEnforceFans = DateTime.Now - _lastFanEnforcement >= FanReEnforceInterval;

        foreach (var fan in phase.AcFans)
        {
            var controller = _acService.Controllers.FirstOrDefault(c => c.Name == fan.Controller);
            if (controller == null)
            {
                _logger.LogWarning("Fan actor: controller {Controller} not configured", fan.Controller);
                continue;
            }

            if (controller.Type == null)
            {
                _logger.LogWarning("Fan actor: controller {Controller} not seen yet - retrying next tick",
                    fan.Controller);
                continue;
            }

            try
            {
                // PortLevels only holds levels we commanded ourselves; an unknown
                // port is treated as mismatch so the level gets enforced
                if (!reEnforceFans &&
                    controller.PortLevels.TryGetValue(fan.Port, out var current) && current == fan.Level)
                    continue;

                _logger.LogInformation("Phase '{Phase}': setting {Controller} port {Port} to level {Level}",
                    phase.Name, fan.Controller, fan.Port, fan.Level);
                await controller.SetLevelAsync(fan.Port, fan.Level);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to set {Controller} port {Port}: {Message}",
                    fan.Controller, fan.Port, ex.Message);
            }
        }

        if (reEnforceFans && phase.AcFans.Count > 0)
            _lastFanEnforcement = DateTime.Now;
    }
}
