namespace Tippy.Core.Models;

public sealed class PedalDeviceProfile
{
    public string DeviceKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Foot control";
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public int SwitchCount { get; set; } = 3;
    public string ArtworkKey { get; set; } = string.Empty;
    public int ActiveBankIndex { get; set; }
    public List<PedalBank> Banks { get; set; } = [];

    public static PedalDeviceProfile Create(
        string deviceKey,
        string displayName,
        int vendorId,
        int productId,
        int switchCount = 3)
    {
        var profile = new PedalDeviceProfile
        {
            DeviceKey = deviceKey,
            DisplayName = displayName,
            VendorId = vendorId,
            ProductId = productId,
            SwitchCount = switchCount
        };
        profile.Normalize();
        return profile;
    }

    public void Normalize()
    {
        SwitchCount = Math.Clamp(SwitchCount, 1, 32);
        ArtworkKey = ArtworkKey?.Trim() ?? string.Empty;
        ActiveBankIndex = Math.Clamp(ActiveBankIndex, 0, AppProfile.MaxBanks - 1);
        Banks ??= [];
        while (Banks.Count < AppProfile.MaxBanks)
        {
            Banks.Add(PedalBank.Create(Banks.Count, SwitchCount));
        }
        if (Banks.Count > AppProfile.MaxBanks)
        {
            Banks.RemoveRange(AppProfile.MaxBanks, Banks.Count - AppProfile.MaxBanks);
        }
        for (var index = 0; index < Banks.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(Banks[index].Name))
            {
                Banks[index].Name = $"Bank {index + 1}";
            }
            Banks[index].EnsureSwitchCount(SwitchCount);
        }
    }
}
