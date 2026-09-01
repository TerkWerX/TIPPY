namespace Tippy.App.Models;

public sealed record HidCandidateInfo(
    string DevicePath,
    string ProductName,
    string Manufacturer,
    int VendorId,
    int ProductId,
    int ReportLength,
    string ReportDescriptorHash,
    bool LooksLikePedal)
{
    public string DisplayName =>
        $"{(LooksLikePedal ? "★ " : string.Empty)}{ProductName}  ·  VID_{VendorId:X4} PID_{ProductId:X4}  ·  {ReportLength} bytes";
}
