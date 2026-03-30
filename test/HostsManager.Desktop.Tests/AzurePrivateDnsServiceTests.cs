namespace HostsManager.Desktop.Tests;

public sealed class AzurePrivateDnsServiceTests
{
    [Theory]
    [InlineData("appcs-sahub-devstage-cus", "privatelink.azconfig.io", false, "appcs-sahub-devstage-cus.privatelink.azconfig.io")]
    [InlineData("appcs-sahub-devstage-cus", "privatelink.azconfig.io", true, "appcs-sahub-devstage-cus.azconfig.io")]
    [InlineData("appcs-sahub-devstage-cus.privatelink.azconfig.io", "privatelink.azconfig.io", true, "appcs-sahub-devstage-cus.azconfig.io")]
    [InlineData("@", "privatelink.azconfig.io", true, "azconfig.io")]
    [InlineData("appcs-sahub-devstage-cus", "azconfig.io", true, "appcs-sahub-devstage-cus.azconfig.io")]
    public void ResolveRecordFqdn_HandlesOptionalPrivatelinkStripping(
        string relativeName,
        string zoneName,
        bool stripPrivatelinkSubdomain,
        string expected)
    {
        var actual = AzurePrivateDnsService.ResolveRecordFqdn(relativeName, zoneName, stripPrivatelinkSubdomain);

        Assert.Equal(expected, actual);
    }
}
