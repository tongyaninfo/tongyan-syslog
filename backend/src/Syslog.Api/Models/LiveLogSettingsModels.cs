using System.Text.Json.Serialization;

namespace Syslog.Api.Models;

public sealed record SavedLiveLogSettings(string Version, int PerDeviceLimit, long MaxBytes);

public sealed record SaveLiveLogSettingsRequest(
    [property: JsonRequired] string Version,
    [property: JsonRequired] int PerDeviceLimit,
    [property: JsonRequired] long MaxBytes);
