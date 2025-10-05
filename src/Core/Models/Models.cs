
namespace ATVCompanion.Core.Models;

public record WakeHint(string Mac, string? BroadcastIp = null, int Port = 9);

public record PairingResult(bool Success, string? Message = null)
{
    public static PairingResult NeedsUserPsk(string msg = "Enter Pre-Shared Key in settings") => new(false, msg);
    public static PairingResult NeedsUserPin(string msg = "Approve pairing/PIN on TV") => new(false, msg);
    public static PairingResult SuccessResult() => new(true, null);
}

public enum PowerStatus { Unknown, Off, On, Standby }

public record TvState(PowerStatus Power, string? Input = null, int? Volume = null);
