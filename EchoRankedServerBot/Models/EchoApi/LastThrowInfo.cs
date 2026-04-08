using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.EchoApi;

public class LastThrowInfo
{
    [JsonPropertyName("arm_speed")]
    public double? ArmSpeed { get; set; }

    [JsonPropertyName("total_speed")]
    public double? TotalSpeed { get; set; }

    [JsonPropertyName("off_axis_spin_deg")]
    public double? OffAxisSpinDeg { get; set; }

    [JsonPropertyName("wrist_throw_penalty")]
    public double? WristThrowPenalty { get; set; }

    [JsonPropertyName("rot_per_sec")]
    public double? RotPerSec { get; set; }

    [JsonPropertyName("pot_speed_from_rot")]
    public double? PotSpeedFromRot { get; set; }

    [JsonPropertyName("speed_from_arm")]
    public double? SpeedFromArm { get; set; }

    [JsonPropertyName("speed_from_movement")]
    public double? SpeedFromMovement { get; set; }

    [JsonPropertyName("speed_from_wrist")]
    public double? SpeedFromWrist { get; set; }

    [JsonPropertyName("wrist_alight_to_throw_deg")]
    public double? WristAlightToThrowDeg { get; set; }

    [JsonPropertyName("throw_align_to_movement_deg")]
    public double? ThrowAlignToMovementDeg { get; set; }

    [JsonPropertyName("off_axis_penalty")]
    public double? OffAxisPenalty { get; set; }

    [JsonPropertyName("throw_move_penalty")]
    public double? ThrowMovePenalty { get; set; }
}
