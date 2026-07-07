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

    /// <summary>
    /// Select the active growth stage; the previous selection is closed
    /// off into the stage history.
    /// A null stage is a full reset for a new grow: it pauses enforcement
    /// and wipes the accumulated stage history.
    /// </summary>
    public async Task SelectStage(string? stage)
    {
        var config = CycleConfig;
        if (config == null || config.ActiveStage == stage) return;

        var now = DateTime.Now;
        if (stage == null)
        {
            config.StageHistory.Clear();
            _logger.LogInformation("Growth stages reset - stage history cleared");
        }
        else if (config.ActiveStage != null && config.ActiveSince != null)
        {
            config.StageHistory.Add(new StageHistoryEntry
            {
                Stage = config.ActiveStage,
                From = config.ActiveSince.Value,
                To = now
            });
            _logger.LogInformation("Growth stage changed to {Stage}", stage);
        }
        else
        {
            _logger.LogInformation("Growth stage changed to {Stage}", stage);
        }

        config.ActiveStage = stage;
        config.ActiveSince = stage != null ? now : null;
        await UpdateConfig(config);
        OnCycleChanged?.Invoke();
    }

    /// <summary>Start time of the next phase change, for display purposes</summary>
    public (string Name, TimeOnly Start)? NextPhase
    {
        get
        {
            var phases = CycleConfig?.ActivePhases;
            if (phases == null || phases.Count == 0) return null;
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var sorted = phases.OrderBy(p => p.Start).ToList();
            var next = sorted.FirstOrDefault(p => p.Start > now) ?? sorted[0];
            return (next.Name, next.Start);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cycle Scheduler starting...");

        // Never give up on a read failure: a disabled scheduler means the grow
        // lights stop switching. Keep retrying so a fixed config file gets
        // picked up without an app restart.
        while (CycleConfig == null)
        {
            try
            {
                CycleConfig = await LightCycleConfig.Read(_configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read cycle configuration from {Path} - retrying in 30s", _configPath);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
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
        if (CycleConfig == null) return;

        var activePhase = CycleConfig.GetActivePhase(TimeOnly.FromDateTime(DateTime.Now));

        // No growth stage selected (or its preset is empty): enforce nothing
        if (activePhase == null)
        {
            if (CurrentPhase != null)
            {
                _logger.LogInformation("No active growth stage - cycle enforcement paused");
                CurrentPhase = null;
                OnCycleChanged?.Invoke();
            }

            return;
        }

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
            // Match by configured name; legacy cycle files still hold a host/IP
            var device = _espService.Devices.FirstOrDefault(d => d.Name == actor.Node || d.Host == actor.Node);
            if (device == null)
            {
                _logger.LogWarning("Actor {ObjectId}: node {Node} not configured", actor.ObjectId, actor.Node);
                continue;
            }

            var connection = device.Connection;
            if (connection == null)
            {
                _logger.LogWarning("Actor {ObjectId}: node {Node} is disconnected", actor.ObjectId, actor.Node);
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

                _logger.LogWarning("Actor {ObjectId} not found on node {Node}", actor.ObjectId, actor.Node);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to set actor {ObjectId} on {Node}: {Message}",
                    actor.ObjectId, actor.Node, ex.Message);
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
