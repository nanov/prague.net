namespace Prague.Tests.Models;

/// <summary>
///   Interface for polymorphic types defined in a separate assembly.
///   This tests that the code generator can find derived types from referenced assemblies.
/// </summary>
public interface IExternalTelemetry {
	string DeviceKind { get; }
	int Score { get; set; }
}

/// <summary>
///   Base class implementing the interface.
/// </summary>
public class ExternalBaseTelemetry : IExternalTelemetry {
	public string? ListingId { get; set; }
	public virtual string DeviceKind => "Unknown";
	public int Score { get; set; }
}

/// <summary>
///   Concrete implementation - Electronics
/// </summary>
public sealed class ExternalElectronicsTelemetry : ExternalBaseTelemetry {
	public override string DeviceKind => "Electronics";
	public int PrimaryValue { get; set; }
	public int SecondaryValue { get; set; }
	public string? CurrentPhase { get; set; }
}

/// <summary>
///   Concrete implementation - Sensor
/// </summary>
public sealed class ExternalSensorTelemetry : ExternalBaseTelemetry {
	public override string DeviceKind => "Sensor";
	public int Tiers { get; set; }
	public int Cycles { get; set; }
	public string? ActiveNode { get; set; }
}

/// <summary>
///   Concrete implementation - Audio
/// </summary>
public sealed class ExternalAudioTelemetry : ExternalBaseTelemetry {
	public override string DeviceKind => "Audio";
	public int Segment { get; set; }
	public int PrimaryPoints { get; set; }
	public int SecondaryPoints { get; set; }
}
