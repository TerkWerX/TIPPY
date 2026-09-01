using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tippy.App.Services;

namespace Tippy.App.Tests;

public sealed class AdvancedOutputsAndSupportPackTests
{
    [Fact]
    public void VirtualGamepadScalesEveryAnalogRange()
    {
        Assert.Equal(short.MinValue, VirtualGamepadService.ToAxisValue(-100));
        Assert.Equal((short)0, VirtualGamepadService.ToAxisValue(0));
        Assert.Equal(short.MaxValue, VirtualGamepadService.ToAxisValue(100));
        Assert.Equal((byte)0, VirtualGamepadService.ToSliderValue(0));
        Assert.Equal(byte.MaxValue, VirtualGamepadService.ToSliderValue(100));
        Assert.Equal("Right Trigger", VirtualGamepadService.NormalizeAxisName("RT"));
    }

    [Fact]
    public void AnalogLedgerRestoresAnotherPedalsStillHeldValue()
    {
        var ledger = new GamepadAnalogLedger();
        Assert.Equal(40, Assert.Single(ledger.Acquire("one", "Left X", 40)).Value);
        Assert.Equal(90, Assert.Single(ledger.Acquire("two", "LX", 90)).Value);
        Assert.Equal(40, Assert.Single(ledger.ReleaseOwner("two")).Value);
        Assert.Equal(0, Assert.Single(ledger.ReleaseOwner("one")).Value);
    }

    [Fact]
    public void OscPacketContainsPaddedAddressAndTypedArguments()
    {
        var packet = OscOutputService.BuildPacket("/tippy", "12,2.5,hello");
        Assert.Equal(0, packet.Length % 4);
        var text = Encoding.UTF8.GetString(packet);
        Assert.Contains("/tippy", text);
        Assert.Contains(",ifs", text);
    }

    [Fact]
    public async Task AuthenticatedSupportPackVerifiesPublisherAndFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tippy-pack-test-{Guid.NewGuid():N}");
        var destination = Path.Combine(root, "library");
        var archivePath = Path.Combine(root, "pack.zip");
        Directory.CreateDirectory(root);
        try
        {
            var bytes = Encoding.UTF8.GetBytes("{\"devices\":[]}");
            var manifest = new DeviceSupportPackManifest
            {
                PackId = "test-pack", Version = "1.2.3", PublisherId = "test-publisher",
                Files = [new DeviceSupportPackFile { Path = "pedal_registry.json", Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }]
            };
            using var rsa = RSA.Create(2048);
            manifest.Signature = Convert.ToBase64String(rsa.SignData(
                DeviceSupportPackService.GetSignaturePayload(manifest), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                await using (var manifestStream = archive.CreateEntry("pack-manifest.json").Open())
                    await JsonSerializer.SerializeAsync(manifestStream, manifest);
                await using var data = archive.CreateEntry("pedal_registry.json").Open();
                await data.WriteAsync(bytes);
            }
            var publisher = new SupportPackPublisher
                { Id = "test-publisher", Name = "Test Publisher", PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem() };
            var service = new DeviceSupportPackService(destination, [publisher]);

            var result = await service.InstallAsync(archivePath, true);

            Assert.True(result.PublisherAuthenticated);
            Assert.Equal("Test Publisher", result.Publisher);
            Assert.True(File.Exists(Path.Combine(destination, "pedal_registry.json")));
            Assert.Equal("1.2.3", Assert.Single(service.GetInstalledPacks()).Version);

            manifest.Version = "9.9.9"; // Signature still covers 1.2.3.
            var tamperedPath = Path.Combine(root, "tampered.zip");
            using (var archive = ZipFile.Open(tamperedPath, ZipArchiveMode.Create))
            {
                await using (var manifestStream = archive.CreateEntry("pack-manifest.json").Open())
                    await JsonSerializer.SerializeAsync(manifestStream, manifest);
                await using var data = archive.CreateEntry("pedal_registry.json").Open();
                await data.WriteAsync(bytes);
            }
            await Assert.ThrowsAsync<CryptographicException>(() => service.InstallAsync(tamperedPath, true));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("2.0.0", "1.9.9", false)]
    [InlineData("v1.2.3", "1.2.3", false)]
    public void PackUpdateComparisonUnderstandsVersions(string installed, string available, bool expected) =>
        Assert.Equal(expected, DeviceSupportPackService.IsUpdateAvailable(installed, available));
}
