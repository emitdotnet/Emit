namespace BuildingSentinel.Common.Simulation;

using BuildingSentinel.Common.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

/// <summary>
/// A hosted background service that simulates realistic building-access activity by sending events to the local API.
/// Starts automatically with the application — no manual input required.
/// </summary>
public sealed class BuildingSimulatorService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<BuildingSimulatorService> logger) : BackgroundService
{
    private static readonly string[] Devices =
    [
        "lobby-north-reader",
        "lobby-south-reader",
        "lobby-turnstile-01",
        "lobby-turnstile-02",
        "lobby-turnstile-03",
        "floor-01-east",
        "floor-01-west",
        "floor-05-east",
        "floor-05-west",
        "floor-10-east",
        "floor-10-west",
        "server-room-main",
        "server-room-backup",
        "parking-gate-in",
        "parking-gate-out",
        "rooftop-access",
        "basement-utility",
        "data-center-cage",
        "executive-suite",
        "cafeteria-side"
    ];

    private static readonly string[] LobbyDevices =
    [
        "lobby-north-reader",
        "lobby-south-reader",
        "lobby-turnstile-01",
        "lobby-turnstile-02",
        "lobby-turnstile-03"
    ];

    private static readonly string[] RestrictedDevices =
    [
        "server-room-main",
        "server-room-backup",
        "data-center-cage"
    ];

    private static readonly string[] HighSecurityDevices =
    [
        "server-room-main",
        "server-room-backup",
        "data-center-cage",
        "executive-suite",
        "rooftop-access"
    ];

    private static readonly string[] DeviceLocations = new Dictionary<string, string>
    {
        ["lobby-north-reader"]   = "Lobby North",
        ["lobby-south-reader"]   = "Lobby South",
        ["lobby-turnstile-01"]   = "Lobby Turnstile 1",
        ["lobby-turnstile-02"]   = "Lobby Turnstile 2",
        ["lobby-turnstile-03"]   = "Lobby Turnstile 3",
        ["floor-01-east"]        = "Floor 1 - East Wing",
        ["floor-01-west"]        = "Floor 1 - West Wing",
        ["floor-05-east"]        = "Floor 5 - East Wing",
        ["floor-05-west"]        = "Floor 5 - West Wing",
        ["floor-10-east"]        = "Floor 10 - East Wing",
        ["floor-10-west"]        = "Floor 10 - West Wing",
        ["server-room-main"]     = "Server Room (Main)",
        ["server-room-backup"]   = "Server Room (Backup)",
        ["parking-gate-in"]      = "Parking Garage - Entry",
        ["parking-gate-out"]     = "Parking Garage - Exit",
        ["rooftop-access"]       = "Rooftop",
        ["basement-utility"]     = "Basement - Utility",
        ["data-center-cage"]     = "Data Center Cage",
        ["executive-suite"]      = "Executive Suite",
        ["cafeteria-side"]       = "Cafeteria - Side Entrance"
    }.Values.ToArray();

    private static readonly Dictionary<string, string> DeviceLocationMap = new()
    {
        ["lobby-north-reader"]   = "Lobby North",
        ["lobby-south-reader"]   = "Lobby South",
        ["lobby-turnstile-01"]   = "Lobby Turnstile 1",
        ["lobby-turnstile-02"]   = "Lobby Turnstile 2",
        ["lobby-turnstile-03"]   = "Lobby Turnstile 3",
        ["floor-01-east"]        = "Floor 1 - East Wing",
        ["floor-01-west"]        = "Floor 1 - West Wing",
        ["floor-05-east"]        = "Floor 5 - East Wing",
        ["floor-05-west"]        = "Floor 5 - West Wing",
        ["floor-10-east"]        = "Floor 10 - East Wing",
        ["floor-10-west"]        = "Floor 10 - West Wing",
        ["server-room-main"]     = "Server Room (Main)",
        ["server-room-backup"]   = "Server Room (Backup)",
        ["parking-gate-in"]      = "Parking Garage - Entry",
        ["parking-gate-out"]     = "Parking Garage - Exit",
        ["rooftop-access"]       = "Rooftop",
        ["basement-utility"]     = "Basement - Utility",
        ["data-center-cage"]     = "Data Center Cage",
        ["executive-suite"]      = "Executive Suite",
        ["cafeteria-side"]       = "Cafeteria - Side Entrance"
    };

    private static readonly string[] EmployeeBadges =
    [
        "emp-001", "emp-002", "emp-003", "emp-004", "emp-005",
        "emp-006", "emp-007", "emp-008", "emp-009", "emp-010",
        "emp-011", "emp-012", "emp-013", "emp-014", "emp-015",
        "emp-016", "emp-017", "emp-018", "emp-019", "emp-020",
        "emp-021", "emp-022", "emp-023", "emp-024", "emp-025"
    ];

    private const string SuspectBadge = "badge-suspect-01";
    private const int SuspectWarningThreshold = 5;

    private readonly Random _random = new();
    private int _suspectDenialCount;
    private int _tickCount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "[Simulator] BuildingSimulatorService starting — building: Acme Corp HQ. Waiting 5 s for server to boot.");

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

        logger.LogInformation("[Simulator] Starting simulation loop.");

        var period = configuration.GetValue<TimeSpan?>("Simulation:Period") ?? TimeSpan.FromMilliseconds(800);
        using var timer = new PeriodicTimer(period);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                _tickCount++;

                if (_tickCount % 20 == 0)
                {
                    await RunMorningRushAsync(stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    await RunScenarioTickAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Simulator] Tick {Tick} failed — continuing.", _tickCount);
            }
        }

        logger.LogInformation("[Simulator] Simulation loop stopped.");
    }

    private async Task RunScenarioTickAsync(CancellationToken ct)
    {
        var roll = _random.Next(100);

        BuildingEventRequest request = roll switch
        {
            < 60 => GenerateNormalAccess(),
            < 75 => GenerateAccessDenied(),
            < 90 => GenerateMotion(),
            _    => GenerateSuspectAttempt()
        };

        await PostEventAsync(request, ct).ConfigureAwait(false);
    }

    private BuildingEventRequest GenerateNormalAccess()
    {
        var device = Devices[_random.Next(Devices.Length)];
        var badge = EmployeeBadges[_random.Next(EmployeeBadges.Length)];
        var location = DeviceLocationMap[device];

        logger.LogInformation(
            "[Simulator] Normal access — badge {Badge} granted at {Device} ({Location})",
            badge, device, location);

        return new BuildingEventRequest(
            DeviceId: device,
            EventType: "access.granted",
            Location: location,
            Metadata: new Dictionary<string, string> { ["badgeId"] = badge });
    }

    private BuildingEventRequest GenerateAccessDenied()
    {
        var device = RestrictedDevices[_random.Next(RestrictedDevices.Length)];
        var badge = EmployeeBadges[_random.Next(EmployeeBadges.Length)];
        var location = DeviceLocationMap[device];

        logger.LogInformation(
            "[Simulator] Access denied — badge {Badge} rejected at restricted device {Device} ({Location})",
            badge, device, location);

        return new BuildingEventRequest(
            DeviceId: device,
            EventType: "access.denied",
            Location: location,
            Metadata: new Dictionary<string, string> { ["badgeId"] = badge });
    }

    private BuildingEventRequest GenerateMotion()
    {
        var device = Devices[_random.Next(Devices.Length)];
        var location = DeviceLocationMap[device];

        logger.LogInformation(
            "[Simulator] Motion detected — sensor triggered at {Device} ({Location})",
            device, location);

        return new BuildingEventRequest(
            DeviceId: "sensor",
            EventType: "motion.detected",
            Location: location,
            Metadata: new Dictionary<string, string> { ["sourceDevice"] = device });
    }

    private BuildingEventRequest GenerateSuspectAttempt()
    {
        var device = HighSecurityDevices[_random.Next(HighSecurityDevices.Length)];
        var location = DeviceLocationMap[device];

        _suspectDenialCount++;

        logger.LogInformation(
            "[Simulator] Suspect attempt — badge {Badge} denied at high-security device {Device} ({Location}) — total suspect denials: {Count}",
            SuspectBadge, device, location, _suspectDenialCount);

        if (_suspectDenialCount == SuspectWarningThreshold)
        {
            logger.LogWarning(
                "[Simulator] Suspect badge {Badge} has triggered the denial threshold after {Count} attempts.",
                SuspectBadge, _suspectDenialCount);
        }

        return new BuildingEventRequest(
            DeviceId: device,
            EventType: "access.denied",
            Location: location,
            Metadata: new Dictionary<string, string> { ["badgeId"] = SuspectBadge });
    }

    private async Task RunMorningRushAsync(CancellationToken ct)
    {
        logger.LogInformation("[RUSH] Morning rush simulation started");

        for (var i = 0; i < 8; i++)
        {
            var device = LobbyDevices[_random.Next(LobbyDevices.Length)];
            var badge = EmployeeBadges[_random.Next(EmployeeBadges.Length)];
            var location = DeviceLocationMap[device];

            logger.LogInformation(
                "[RUSH] Rush event {Index}/8 — badge {Badge} granted at {Device} ({Location})",
                i + 1, badge, device, location);

            var request = new BuildingEventRequest(
                DeviceId: device,
                EventType: "access.granted",
                Location: location,
                Metadata: new Dictionary<string, string> { ["badgeId"] = badge });

            await PostEventAsync(request, ct).ConfigureAwait(false);

            if (i < 7)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task PostEventAsync(BuildingEventRequest request, CancellationToken ct)
    {
        var baseUrl = configuration["Simulation:ApiBaseUrl"] ?? "http://localhost:5000";
        var client = httpClientFactory.CreateClient("self");

        try
        {
            var response = await client
                .PostAsJsonAsync($"{baseUrl}/api/events", request, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "[Simulator] HTTP error posting event {EventType} from {DeviceId} — server may not be ready.",
                request.EventType, request.DeviceId);
        }
    }
}
